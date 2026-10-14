using System.Linq;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.Overlays;
using UnityEditor.ShortcutManagement;
using UnityEngine;
using UnityEngine.Rendering;
using WFCContent.World;

namespace Editor
{
    // outil de la vue Scene (planete selectionnee): peindre a la souris sur la planete.
    // clic = peindre, Shift = effacer, Ctrl = pipette, [ ] / Ctrl + molette = taille, 0-9 = hauteur (outil Relief)
    [EditorTool("WFC Planet Painter", typeof(GoldbergPolyhedron))]
    public class WFCPaintTool : EditorTool
    {
        private static PaintStroke stroke;
        private static double nextLiveSolve;

        private static PaintHit hover;
        private static bool hasHover;
        private static GoldbergPolyhedron hoverPlanet;
        private static Vector3 lastCameraPosition;
        private static Quaternion lastCameraRotation;

        private GUIContent icon;

        public override GUIContent toolbarIcon =>
            icon ??= new GUIContent(WFCPaintGUI.Icon("TerrainInspector.TerrainToolSplat"), "WFC Planet Painter (Shift+B)");

        [Shortcut("WFC/Planet Painter", typeof(SceneView), KeyCode.B, ShortcutModifiers.Shift)]
        private static void ShortcutActivate() => Activate(null);

        // selectionne la planete (celle donnee, sinon la selection, sinon la premiere de la scene) et prend l'outil
        public static void Activate(GoldbergPolyhedron planet)
        {
            planet ??= Selection.GetFiltered<GoldbergPolyhedron>(SelectionMode.Editable).FirstOrDefault()
                       ?? Object.FindObjectsByType<GoldbergPolyhedron>().FirstOrDefault();
            if (planet == null)
            {
                Debug.LogWarning("[WFC Paint] Aucune planète (GoldbergPolyhedron) dans la scène.");
                return;
            }
            Selection.activeGameObject = planet.gameObject;
            // un outil de composant n'existe qu'une fois la selection prise en compte par l'editeur (quelques tours plus tard):
            // on reessaie jusqu'a ce qu'il soit actif
            int attempts = 0;
            void TryActivate()
            {
                if (IsActive || ++attempts > 60 || planet == null || Selection.activeGameObject != planet.gameObject)
                {
                    EditorApplication.update -= TryActivate;
                    return;
                }
                // la liste des outils de composant suit les editeurs de la selection (reconstruits par l'Inspector, pas
                // forcement visible)
                ActiveEditorTracker.sharedTracker.ForceRebuild();
                try
                {
                    ToolManager.SetActiveTool<WFCPaintTool>();
                }
                catch (System.InvalidOperationException e)
                {
                    if (attempts == 60)
                        Debug.LogException(e);
                }
            }
            EditorApplication.update += TryActivate;
        }

        public static bool IsActive => ToolManager.activeToolType == typeof(WFCPaintTool);

        public override void OnActivated()
        {
            if (target is GoldbergPolyhedron planet)
            {
                try
                {
                    WFCPaintOps.EnsureReady(planet);
                }
                catch (System.Exception e)
                {
                    Debug.LogException(e, planet);
                }
            }
            SceneView.lastActiveSceneView?.ShowNotification(
                new GUIContent("Clic: peindre · Shift: effacer · Ctrl: pipette · [ ] ou Ctrl+molette: taille"), 3);
        }

        public override void OnWillBeDeactivated() => FinishStroke();

        public override void OnToolGUI(EditorWindow window)
        {
            if (window is not SceneView sceneView || target is not GoldbergPolyhedron planet)
                return;
            var settings = WFCPaintSettings.instance;
            var painting = planet.GetComponent<WFCPlanetPainting>();
            var evt = Event.current;
            int id = GUIUtility.GetControlID(FocusType.Passive);
            if (evt.type == EventType.Layout)
                HandleUtility.AddDefaultControl(id);

            if (Shortcuts(evt, settings, planet))
                return;

            bool ready = WFCPaintOps.IsReady(planet, painting);
            if (ready)
                WFCPaintVisuals.Draw(planet, painting, settings);
            UpdateHover(sceneView, planet, painting, evt, ready);

            double now = EditorApplication.timeSinceStartup;
            switch (evt.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (evt.button != 0 || evt.alt)
                        break;
                    if (!ready)
                    {
                        painting = WFCPaintOps.EnsureReady(planet);
                        Raycast(planet, painting, evt);
                    }
                    if (!hasHover)
                        break;
                    if (evt.control || evt.command)
                    {
                        var picked = WFCPaintOps.Pick(planet, painting, settings, hover);
                        if (picked != null)
                            sceneView.ShowNotification(new GUIContent(picked), 1);
                        evt.Use();
                        break;
                    }
                    FinishStroke();
                    stroke = WFCPaintOps.BeginStroke(planet, painting, settings, hover, evt.shift);
                    nextLiveSolve = now + 0.08;
                    GUIUtility.hotControl = id;
                    evt.Use();
                    break;

                case EventType.MouseDrag:
                    if (GUIUtility.hotControl != id || stroke == null)
                        break;
                    if (hasHover)
                        WFCPaintOps.DragTo(stroke, settings, hover);
                    if (settings.liveSolve && now >= nextLiveSolve && stroke.pending.Count > 0)
                    {
                        var watch = System.Diagnostics.Stopwatch.StartNew();
                        WFCPaintOps.ResolvePending(stroke, settings);
                        // la planete doit rester fluide: on espace les resolutions si elles coutent cher
                        nextLiveSolve = EditorApplication.timeSinceStartup + Mathf.Max(0.12f, watch.ElapsedMilliseconds * 0.0015f);
                    }
                    evt.Use();
                    break;

                case EventType.MouseUp:
                    if (GUIUtility.hotControl != id)
                        break;
                    GUIUtility.hotControl = 0;
                    FinishStroke();
                    evt.Use();
                    break;

                case EventType.MouseMove:
                    sceneView.Repaint();
                    break;

                case EventType.MouseLeaveWindow:
                    hasHover = false;
                    sceneView.Repaint();
                    break;

                case EventType.Repaint:
                    if (hasHover && ready)
                        DrawCursor(planet, painting, settings, evt);
                    break;
            }
        }

        private static void FinishStroke()
        {
            if (stroke == null)
                return;
            var finished = stroke;
            stroke = null;
            try
            {
                WFCPaintOps.EndStroke(finished, WFCPaintSettings.instance);
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
            }
        }

        #region Survol

        private static void UpdateHover(SceneView sceneView, GoldbergPolyhedron planet, WFCPlanetPainting painting, Event evt, bool ready)
        {
            var cameraTransform = sceneView.camera.transform;
            bool mouse = evt.type is EventType.MouseMove or EventType.MouseDrag or EventType.MouseDown;
            bool cameraMoved = cameraTransform.position != lastCameraPosition || cameraTransform.rotation != lastCameraRotation;
            if (!mouse && !(evt.type == EventType.Repaint && (cameraMoved || hoverPlanet != planet)))
                return;
            lastCameraPosition = cameraTransform.position;
            lastCameraRotation = cameraTransform.rotation;
            hoverPlanet = planet;
            if (!ready)
            {
                hasHover = false;
                return;
            }
            Raycast(planet, painting, evt);
        }

        private static void Raycast(GoldbergPolyhedron planet, WFCPlanetPainting painting, Event evt)
        {
            var ray = HandleUtility.GUIPointToWorldRay(evt.mousePosition);
            hasHover = painting != null && WFCPaintRaycast.Raycast(planet, ray, WFCPaintOps.Instances(planet, painting), out hover);
        }

        #endregion

        #region Raccourcis

        private static bool Shortcuts(Event evt, WFCPaintSettings settings, GoldbergPolyhedron planet)
        {
            if (evt.type == EventType.ScrollWheel && (evt.control || evt.command))
            {
                settings.size *= evt.delta.y > 0f ? 1f / 1.15f : 1.15f;
                settings.Changed();
                evt.Use();
                return true;
            }
            if (evt.type != EventType.KeyDown)
                return false;

            bool smaller = evt.character == '[' || evt.keyCode == KeyCode.LeftBracket || evt.keyCode == KeyCode.KeypadMinus;
            bool bigger = evt.character == ']' || evt.keyCode == KeyCode.RightBracket || evt.keyCode == KeyCode.KeypadPlus;
            if (smaller || bigger)
            {
                settings.size *= bigger ? 1.2f : 1f / 1.2f;
                settings.Changed();
                evt.Use();
                return true;
            }

            // AltGr = Ctrl + Alt: [ et ] sur un clavier AZERTY passent par les touches de chiffres
            if (evt.alt || evt.control || evt.command)
                return false;
            int digit = evt.keyCode switch
            {
                >= KeyCode.Alpha0 and <= KeyCode.Alpha9 => evt.keyCode - KeyCode.Alpha0,
                >= KeyCode.Keypad0 and <= KeyCode.Keypad9 => evt.keyCode - KeyCode.Keypad0,
                _ => -1
            };
            if (digit >= 0 && settings.tool == PaintTool.Relief)
            {
                settings.height = Mathf.Min(digit, planet.Floors + 1);
                settings.reliefOp = ReliefOp.Paint;
                settings.Changed();
                evt.Use();
                return true;
            }
            if (evt.keyCode == KeyCode.Escape && stroke == null)
            {
                ToolManager.RestorePreviousPersistentTool();
                evt.Use();
                return true;
            }
            return false;
        }

        #endregion

        #region Curseur

        private static void DrawCursor(GoldbergPolyhedron planet, WFCPlanetPainting painting, WFCPaintSettings settings, Event evt)
        {
            var grid = planet.Grid;
            bool erase = evt.shift || settings.tool == PaintTool.Erase;
            var color = ToolColor(settings, planet, erase);
            var up = planet.transform.TransformDirection(hover.direction);
            float scale = planet.transform.lossyScale.x;
            var zTest = Handles.zTest;
            Handles.zTest = CompareFunction.Always;

            if (settings.shape == BrushShape.Circle)
            {
                float radius = settings.size * grid.MeanEdge * scale;
                Handles.color = new Color(color.r, color.g, color.b, 0.08f);
                Handles.DrawSolidDisc(hover.point, up, radius);
                Handles.color = color;
                Handles.DrawWireDisc(hover.point, up, radius, 2f);
            }

            // ce que le pinceau va toucher
            Handles.color = color;
            bool vertices = settings.tool == PaintTool.Relief || (settings.tool == PaintTool.Erase && settings.eraseRelief);
            var solved = painting.SolvedVertexHeights(grid);
            float dot = grid.MeanEdge * scale * 0.07f;
            if (vertices)
            {
                var targets = settings.shape == BrushShape.Fill ? new System.Collections.Generic.List<int> { hover.vertex }
                    : settings.tool == PaintTool.Relief && !erase ? WFCPaintOps.ReliefVertices(planet, settings, hover)
                    : WFCPaintOps.BrushVertices(grid, settings, hover);
                foreach (int v in targets.Take(600))
                {
                    var p = WorldSurface(planet, grid, solved, v);
                    Handles.DrawSolidDisc(p, planet.transform.TransformDirection(grid.Points[v].normalized), dot);
                }
            }
            if (settings.tool != PaintTool.Relief)
            {
                var slots = settings.shape == BrushShape.Fill ? new System.Collections.Generic.List<int> { hover.slot }
                    : WFCPaintOps.BrushSlots(grid, settings, hover);
                foreach (int s in slots.Take(400))
                {
                    var t = grid.Triangles[s];
                    Vector3 a = WorldSurface(planet, grid, solved, t[0]), b = WorldSurface(planet, grid, solved, t[1]),
                        c = WorldSurface(planet, grid, solved, t[2]);
                    Handles.DrawAAPolyLine(2f, a, b, c, a);
                }
            }
            Handles.zTest = zTest;

            // etiquette a cote de la souris
            Handles.BeginGUI();
            var label = new GUIContent(CursorLabel(settings, planet, painting, erase));
            var style = new GUIStyle(EditorStyles.helpBox) { fontSize = 11, richText = true };
            var size = style.CalcSize(label);
            GUI.Label(new Rect(evt.mousePosition.x + 18, evt.mousePosition.y + 14, size.x + 4, size.y), label, style);
            Handles.EndGUI();
        }

        private static Vector3 WorldSurface(GoldbergPolyhedron planet, PlanetGrid grid, int[] solved, int vertex)
        {
            float floorStep = planet.LayerHeight * planet.FloorHeight;
            var local = grid.Points[vertex] * (1f + floorStep * solved[vertex] + planet.LayerHeight * 0.1f);
            return planet.transform.TransformPoint(local);
        }

        private static Color ToolColor(WFCPaintSettings settings, GoldbergPolyhedron planet, bool erase)
        {
            if (erase)
                return new Color(1f, 0.35f, 0.3f);
            return settings.tool switch
            {
                PaintTool.Relief => settings.reliefOp == ReliefOp.Paint ? WFCPaintGUI.HeightColor(settings.height, planet.Floors) : Color.white,
                PaintTool.Stamp => WFCPaintVisuals.StampColor,
                PaintTool.Zone => settings.zone != null ? settings.zone.color : Color.white,
                PaintTool.Lock => WFCPaintVisuals.LockColor,
                PaintTool.Reroll => new Color(1f, 0.85f, 0.3f),
                _ => Color.white
            };
        }

        private static string CursorLabel(WFCPaintSettings settings, GoldbergPolyhedron planet, WFCPlanetPainting painting, bool erase)
        {
            string text = settings.tool switch
            {
                PaintTool.Relief when erase => "Effacer le relief",
                PaintTool.Relief => settings.reliefOp switch
                {
                    ReliefOp.Paint => WFCPaintGUI.HeightLabel(settings.height, planet.Floors, WFCPaintGUI.HasHoles(planet)),
                    ReliefOp.Raise => "Monter d'un étage",
                    ReliefOp.Lower => "Descendre d'un étage",
                    ReliefOp.Flatten => "Aplanir",
                    _ => "Lisser"
                } + (settings.force ? " · imposer" : " · guider"),
                PaintTool.Stamp when erase => "Effacer les stamps",
                PaintTool.Stamp => settings.stamps.Count == 0 ? "Choisis un stamp (palette)"
                    : string.Join(", ", settings.stamps.Take(3).Select(WFCPaintGUI.PrettyName)) + (settings.stamps.Count > 3 ? "…" : ""),
                PaintTool.Zone when erase => "Effacer les zones",
                PaintTool.Zone => settings.zone != null ? settings.zone.name.Replace("Zone_", "Zone ") : "Choisis une zone",
                PaintTool.Lock => erase ? "Dégeler" : "Geler",
                PaintTool.Reroll => "Relancer",
                _ => "Gomme"
            };

            // ce qu'il y a sous la souris
            var grid = planet.Grid;
            if (painting.Solution.Count == grid.SlotCount && hover.slot >= 0)
            {
                var key = painting.Solution[hover.slot];
                string under = WFCPaintGUI.PrettyName(key.element);
                if (painting.GetLock(hover.slot) != null)
                    under += " (gelé)";
                var zone = painting.GetZone(hover.slot)?.zone;
                if (zone != null)
                    under += " · " + zone.name.Replace("Zone_", "");
                text += $"\n<size=9>{under}</size>";
            }
            return text;
        }

        #endregion
    }

    // reglages rapides dans la vue Scene, visibles quand l'outil est actif
    [Overlay(typeof(SceneView), "wfc-planet-painter", "WFC Planet Painter", true)]
    public class WFCPaintOverlay : IMGUIOverlay, ITransientOverlay
    {
        public bool visible => WFCPaintTool.IsActive;

        public override void OnGUI()
        {
            var settings = WFCPaintSettings.instance;
            var planet = Selection.GetFiltered<GoldbergPolyhedron>(SelectionMode.Editable).FirstOrDefault();
            var painting = planet != null ? planet.GetComponent<WFCPlanetPainting>() : null;
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(290)))
            {
                WFCPaintGUI.ToolBar(settings, true);
                EditorGUILayout.Space(2);
                WFCPaintGUI.BrushControls(settings);
                WFCPaintGUI.ToolOptions(settings, planet, painting, true);
                EditorGUILayout.Space(2);
                WFCPaintGUI.SolveControls(settings);
                WFCPaintGUI.Status(planet);
                if (GUILayout.Button("Fenêtre Planet Painter…", EditorStyles.miniButton))
                    WFCPaintWindow.Open();
            }
        }
    }
}
