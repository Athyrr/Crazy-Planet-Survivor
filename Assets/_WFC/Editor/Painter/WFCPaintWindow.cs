using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;
using WFCContent.WFC;
using WFCContent.World;

namespace Editor
{
    // editeur complet de la peinture: outils, palette de stamps, zones et leurs regles, calques, actions sur la planete
    public class WFCPaintWindow : EditorWindow
    {
        private static readonly string[] ModeLabels = { "Favoriser", "Éviter", "Interdire" };
        private static readonly Color[] ModeColors = { new(0.35f, 0.8f, 0.4f), new(0.95f, 0.7f, 0.3f), new(0.9f, 0.3f, 0.3f) };

        [SerializeField] private GoldbergPolyhedron planet;
        [SerializeField] private bool foldTool = true, foldPalette = true, foldZones = true, foldLayers = true, foldPlanet = true;
        private Vector2 scroll;

        // zone editee: re-resolution des slots peints avec elle, une fois les clics finis
        private WFCPaintZone pendingZone;
        private double pendingZoneAt = -1;

        [MenuItem("Tools/WFC/Planet Painter")]
        public static void Open()
        {
            var window = GetWindow<WFCPaintWindow>("Planet Painter");
            window.minSize = new Vector2(340, 420);
        }

        private void OnEnable()
        {
            Selection.selectionChanged += Repaint;
            ToolManager.activeToolChanged += Repaint;
            Undo.undoRedoPerformed += Repaint;
            EditorApplication.update += Tick;
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= Repaint;
            ToolManager.activeToolChanged -= Repaint;
            Undo.undoRedoPerformed -= Repaint;
            EditorApplication.update -= Tick;
        }

        private void Tick()
        {
            if (pendingZone == null || EditorApplication.timeSinceStartup < pendingZoneAt)
                return;
            var zone = pendingZone;
            pendingZone = null;
            foreach (var painting in FindObjectsByType<WFCPlanetPainting>())
            {
                var slots = painting.SlotsWithZone(zone);
                var target = painting.GetComponent<GoldbergPolyhedron>();
                if (slots.Count > 0 && target != null && WFCPaintOps.IsReady(target, painting))
                    WFCPaintOps.ResolveSlots(target, painting, slots, "WFC Paint: règles de zone");
            }
            Repaint();
        }

        private void OnGUI()
        {
            var settings = WFCPaintSettings.instance;
            var selected = Selection.GetFiltered<GoldbergPolyhedron>(SelectionMode.Editable).FirstOrDefault();
            if (selected != null)
                planet = selected;
            if (planet == null)
                planet = FindObjectsByType<GoldbergPolyhedron>().FirstOrDefault();

            DrawHeader();
            if (planet == null)
            {
                EditorGUILayout.HelpBox("Aucune planète (GoldbergPolyhedron) dans la scène.", MessageType.Info);
                return;
            }
            var painting = planet.GetComponent<WFCPlanetPainting>();

            scroll = EditorGUILayout.BeginScrollView(scroll);

            foldTool = Section(foldTool, "Pinceau");
            if (foldTool)
            {
                WFCPaintGUI.ToolBar(settings, false);
                EditorGUILayout.Space(2);
                WFCPaintGUI.BrushControls(settings);
                WFCPaintGUI.ToolOptions(settings, planet, painting, false);
                EditorGUILayout.Space(2);
                WFCPaintGUI.SolveControls(settings);
                WFCPaintGUI.Status(planet);
                EditorGUILayout.LabelField("Clic: peindre · Shift: effacer · Ctrl: pipette · [ ] ou Ctrl+molette: taille · 0-9: hauteur",
                    EditorStyles.centeredGreyMiniLabel);
            }

            foldPalette = Section(foldPalette, "Palette de stamps");
            if (foldPalette)
                DrawPalette(settings);

            foldZones = Section(foldZones, "Zones");
            if (foldZones)
                DrawZones(settings, painting);

            foldLayers = Section(foldLayers, "Calques");
            if (foldLayers)
                WFCPaintGUI.LayerControls(settings, planet, painting);

            foldPlanet = Section(foldPlanet, "Planète");
            if (foldPlanet)
                DrawPlanet(painting);

            EditorGUILayout.EndScrollView();

            if (AssetPreview.IsLoadingAssetPreviews())
                Repaint();
        }

        private void DrawHeader()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                planet = (GoldbergPolyhedron)EditorGUILayout.ObjectField(planet, typeof(GoldbergPolyhedron), true, GUILayout.MinWidth(120));
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(planet == null))
                {
                    bool active = WFCPaintTool.IsActive;
                    var content = new GUIContent(active ? " Peinture active" : " Peindre", WFCPaintGUI.Icon("TerrainInspector.TerrainToolSplat"),
                        "Outil de la vue Scene (Shift+B)");
                    bool now = GUILayout.Toggle(active, content, EditorStyles.toolbarButton, GUILayout.Width(120));
                    if (now != active)
                    {
                        if (now)
                            WFCPaintTool.Activate(planet);
                        else
                            ToolManager.RestorePreviousPersistentTool();
                    }
                }
            }
        }

        private static bool Section(bool open, string title)
        {
            EditorGUILayout.Space(4);
            return EditorGUILayout.Foldout(open, title, true, EditorStyles.foldoutHeader);
        }

        private void DrawPalette(WFCPaintSettings settings)
        {
            var elements = WFCPaintGUI.PlanetElements(planet);
            if (elements.Count == 0)
            {
                EditorGUILayout.HelpBox("Le thème de la planète n'a aucun stamp.", MessageType.Info);
                return;
            }
            EditorGUILayout.LabelField("Clic: choisir · Ctrl/Shift + clic: plusieurs stamps (le WFC choisit parmi eux) · chiffres = hauteurs des coins",
                EditorStyles.wordWrappedMiniLabel);
            WFCPaintGUI.StampPalette(settings, elements, position.width - 24f);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(settings.stamps.Count == 0))
                {
                    if (GUILayout.Button("Vider la sélection", EditorStyles.miniButton))
                    {
                        settings.stamps.Clear();
                        settings.Changed();
                    }
                    if (GUILayout.Button(new GUIContent("Zone depuis la sélection", "Nouvelle zone qui favorise les stamps choisis"),
                            EditorStyles.miniButton))
                    {
                        settings.zone = WFCPaintGUI.CreateZone("Zone_" + WFCPaintGUI.PrettyName(settings.stamps[0]), settings.stamps);
                        settings.tool = PaintTool.Zone;
                        settings.Changed();
                    }
                }
            }
        }

        #region Zones

        private void DrawZones(WFCPaintSettings settings, WFCPlanetPainting painting)
        {
            var zones = WFCPaintGUI.AllZones();
            foreach (var zone in zones)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    var rect = GUILayoutUtility.GetRect(14, 18, GUILayout.Width(14));
                    EditorGUI.DrawRect(new Rect(rect.x, rect.y + 2, 14, 14), zone.color);
                    bool on = settings.zone == zone;
                    if (GUILayout.Toggle(on, zone.name.Replace("Zone_", ""), EditorStyles.miniButton) && !on)
                    {
                        settings.zone = zone;
                        settings.tool = PaintTool.Zone;
                        settings.Changed();
                    }
                    int count = painting != null ? painting.SlotsWithZone(zone).Count : 0;
                    GUILayout.Label(count > 0 ? $"{count} slots" : "", EditorStyles.miniLabel, GUILayout.Width(60));
                }
            }
            if (GUILayout.Button("+ Nouvelle zone", EditorStyles.miniButton))
            {
                settings.zone = WFCPaintGUI.CreateZone("Zone_Nouvelle", null);
                settings.tool = PaintTool.Zone;
                settings.Changed();
            }

            if (settings.zone != null)
                DrawZoneEditor(settings.zone);
        }

        private void DrawZoneEditor(WFCPaintZone zone)
        {
            EditorGUILayout.Space(4);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    string newName = EditorGUILayout.DelayedTextField(zone.name.Replace("Zone_", ""), EditorStyles.boldLabel);
                    if (EditorGUI.EndChangeCheck() && !string.IsNullOrWhiteSpace(newName))
                        AssetDatabase.RenameAsset(AssetDatabase.GetAssetPath(zone), "Zone_" + newName.Trim());
                    if (GUILayout.Button("Asset", EditorStyles.miniButton, GUILayout.Width(48)))
                        EditorGUIUtility.PingObject(zone);
                }

                EditorGUI.BeginChangeCheck();
                var color = EditorGUILayout.ColorField("Couleur", zone.color);
                float strength = EditorGUILayout.Slider(new GUIContent("Force", "Favoriser = poids x force, Éviter = poids / force"),
                    zone.strength, 1.5f, 100f);
                bool onlyFavored = EditorGUILayout.ToggleLeft(new GUIContent("Seulement les stamps favorisés",
                    "Les autres stamps sont évités (poids / force²), sauf s'ils sont seuls à tenir"), zone.onlyFavored);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(zone, "Zone WFC");
                    bool colorOnly = zone.strength == strength && zone.onlyFavored == onlyFavored;
                    zone.color = color;
                    zone.strength = strength;
                    zone.onlyFavored = onlyFavored;
                    EditorUtility.SetDirty(zone);
                    WFCPaintVisuals.Invalidate();
                    SceneView.RepaintAll();
                    if (!colorOnly)
                        ScheduleZoneResolve(zone);
                }

                EditorGUILayout.LabelField("Clic sur un stamp: neutre → favoriser → éviter → interdire", EditorStyles.wordWrappedMiniLabel);
                DrawRuleGrid(zone, WFCPaintGUI.PlanetElements(planet));
            }
        }

        private void DrawRuleGrid(WFCPaintZone zone, List<WFCElementData> elements)
        {
            const float cell = 58f;
            float width = position.width - 40f;
            int columns = Mathf.Max(1, Mathf.FloorToInt(width / (cell + 4f)));
            int rows = Mathf.CeilToInt(elements.Count / (float)columns);
            var area = GUILayoutUtility.GetRect(width, rows * (cell + 30f));
            var nameStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.UpperLeft, clipping = TextClipping.Clip };
            var modeStyle = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.UpperCenter };

            for (int i = 0; i < elements.Count; i++)
            {
                var element = elements[i];
                var rect = new Rect(area.x + (i % columns) * (cell + 4f), area.y + (i / columns) * (cell + 30f), cell, cell);
                var rule = zone.Find(element);
                if (rule != null)
                    EditorGUI.DrawRect(new Rect(rect.x - 2, rect.y - 2, rect.width + 4, rect.height + 4), ModeColors[(int)rule.mode]);
                WFCPaintGUI.Thumbnail(rect, element);
                var name = WFCPaintGUI.PrettyName(element);
                GUI.Label(new Rect(rect.x - 2, rect.yMax, cell + 4, 14), new GUIContent(name, name), nameStyle);
                if (rule != null)
                {
                    modeStyle.normal.textColor = ModeColors[(int)rule.mode];
                    GUI.Label(new Rect(rect.x - 2, rect.yMax + 12, cell + 4, 14), ModeLabels[(int)rule.mode], modeStyle);
                }

                var evt = Event.current;
                if (evt.type == EventType.MouseDown && evt.button == 0 && rect.Contains(evt.mousePosition))
                {
                    WFCPaintZone.RuleMode? next = rule == null ? WFCPaintZone.RuleMode.Favor
                        : rule.mode == WFCPaintZone.RuleMode.Forbid ? null
                        : rule.mode + 1;
                    Undo.RecordObject(zone, "Règle de zone WFC");
                    zone.SetRule(element, next);
                    EditorUtility.SetDirty(zone);
                    ScheduleZoneResolve(zone);
                    evt.Use();
                    Repaint();
                }
            }
        }

        private void ScheduleZoneResolve(WFCPaintZone zone)
        {
            if (pendingZone != null && pendingZone != zone)
                Tick();
            pendingZone = zone;
            pendingZoneAt = EditorApplication.timeSinceStartup + 0.4;
        }

        #endregion

        private void DrawPlanet(WFCPlanetPainting painting)
        {
            if (painting == null)
            {
                EditorGUILayout.HelpBox("Cette planète n'est pas encore peinte. Le pinceau ajoute le composant WFCPlanetPainting " +
                                        "(et régénère la planète une fois si besoin).", MessageType.Info);
                if (GUILayout.Button("Préparer la planète"))
                    WFCPaintOps.EnsureReady(planet);
                return;
            }
            if (!WFCPaintOps.IsReady(planet, painting))
                EditorGUILayout.HelpBox("Les réglages de la planète ont changé depuis la dernière génération: le premier coup de pinceau " +
                                        "la régénère (la peinture suit la nouvelle grille).", MessageType.Warning);

            EditorGUILayout.LabelField($"{planet.LastSlotCount} slots · {planet.ConflictSlots.Count} conflit(s) · graine {planet.Seed}",
                EditorStyles.miniLabel);
            WFCPaintGUI.Actions(planet, painting);
        }
    }

    [CustomEditor(typeof(WFCPlanetPainting))]
    public class WFCPlanetPaintingEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var painting = (WFCPlanetPainting)target;
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("reuseInPlayMode"),
                new GUIContent("Garder en jeu", "En jeu, Generate reconstruit la planète telle qu'elle a été peinte"));
            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.LabelField($"Relief {painting.Count(WFCPlanetPainting.Layer.Relief)} · Stamps {painting.Count(WFCPlanetPainting.Layer.Stamps)} · " +
                                       $"Zones {painting.Count(WFCPlanetPainting.Layer.Zones)} · Gel {painting.Count(WFCPlanetPainting.Layer.Locks)}",
                EditorStyles.miniLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Peindre (Shift+B)"))
                    WFCPaintTool.Activate(painting.GetComponent<GoldbergPolyhedron>());
                if (GUILayout.Button("Planet Painter…"))
                    WFCPaintWindow.Open();
            }
        }
    }
}
