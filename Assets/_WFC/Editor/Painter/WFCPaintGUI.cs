using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using WFCContent.WFC;
using WFCContent.World;

namespace Editor
{
    // widgets partages par l'overlay de la vue Scene (compact) et la fenetre Planet Painter (complet)
    public static class WFCPaintGUI
    {
        public const string ZoneFolder = "Assets/_WFC/WFCContent/Zones";

        private static readonly (PaintTool tool, string label, string icon, string tooltip)[] ToolButtons =
        {
            (PaintTool.Relief, "Relief", "TerrainInspector.TerrainToolRaise", "Hauteur des sommets: mer, étages, montagnes"),
            (PaintTool.Stamp, "Stamp", "TerrainInspector.TerrainToolTrees", "Impose les stamps choisis dans la palette"),
            (PaintTool.Zone, "Zone", "TerrainInspector.TerrainToolSplat", "Peint une zone: ses règles changent les chances des stamps"),
            (PaintTool.Lock, "Geler", "LockIcon-On", "Fige les stamps posés: ils ne bougent plus"),
            (PaintTool.Reroll, "Relancer", "Refresh", "Nouveau tirage du WFC sous le pinceau"),
            (PaintTool.Erase, "Gomme", "Grid.EraserTool|TreeEditor.Trash", "Efface la peinture (Shift avec n'importe quel outil)")
        };

        private static readonly string[] ShapeLabels = { "Cercle", "Cellule", "Remplir" };
        private static readonly string[] ShapeTooltips =
        {
            "Tout ce qui est sous le cercle", "Un seul sommet / slot", "Pot de peinture: la région connexe de même hauteur / même stamp"
        };
        private static readonly string[] ReliefOpLabels = { "Peindre", "Monter", "Descendre", "Aplanir", "Lisser" };
        private static readonly string[] RotationLabels = { "Auto", "0", "1", "2" };

        private static readonly Color[] FloorColors =
        {
            new(0.25f, 0.55f, 1.00f), // mer
            new(0.38f, 0.82f, 0.35f),
            new(0.95f, 0.85f, 0.30f),
            new(1.00f, 0.55f, 0.20f),
            new(0.90f, 0.32f, 0.30f),
            new(0.78f, 0.38f, 0.88f)
        };
        private static readonly Color MountainColor = new(0.96f, 0.96f, 0.96f);

        public static Color HeightColor(int height, int floors) =>
            height > floors ? MountainColor : FloorColors[Mathf.Clamp(height, 0, FloorColors.Length - 1)];

        // planete a trous (theme Alien): le niveau 0 est un trou, pas la mer
        public static bool HasHoles(GoldbergPolyhedron planet)
        {
            var theme = planet != null ? planet.ActiveTheme : null;
            return theme != null && theme.holesAnyLevel;
        }

        public static string HeightLabel(int height, int floors, bool holes = false)
        {
            if (height <= 0)
                return holes ? "Trou" : "Mer";
            if (height > floors)
                return "Montagne";
            return floors == 1 ? "Terre" : $"Étage {height}";
        }

        private static readonly Dictionary<string, Texture> IconCache = new();

        // icone de l'editeur sans message d'erreur si elle n'existe pas (IconContent en log un). "a|b" = a, sinon b
        public static Texture Icon(string names)
        {
            if (IconCache.TryGetValue(names, out var cached) && cached != null)
                return cached;
            Texture found = null;
            foreach (var name in names.Split('|'))
            {
                found = (EditorGUIUtility.isProSkin ? EditorGUIUtility.FindTexture("d_" + name) : null) ?? EditorGUIUtility.FindTexture(name);
                if (found != null)
                    break;
            }
            IconCache[names] = found;
            return found;
        }

        public static string PrettyName(WFCElementData element) =>
            element == null ? "(aucun)" : element.name.Replace("SO_WFC_", "");

        #region Outils

        public static void ToolBar(WFCPaintSettings s, bool compact)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                foreach (var (tool, label, icon, tooltip) in ToolButtons)
                {
                    var image = Icon(icon);
                    var content = compact
                        ? new GUIContent(image, label + " : " + tooltip)
                        : new GUIContent(" " + label, image, tooltip);
                    if (image == null)
                        content = new GUIContent(compact ? label.Substring(0, 2) : label, tooltip);
                    bool on = s.tool == tool;
                    bool now = GUILayout.Toggle(on, content, EditorStyles.miniButton, GUILayout.Height(compact ? 24 : 26),
                        compact ? GUILayout.Width(34) : GUILayout.MinWidth(60));
                    if (now && !on)
                    {
                        s.tool = tool;
                        s.Changed();
                    }
                }
            }
        }

        public static void BrushControls(WFCPaintSettings s)
        {
            EditorGUI.BeginChangeCheck();
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Forme", GUILayout.Width(52));
                var contents = ShapeLabels.Select((l, i) => new GUIContent(l, ShapeTooltips[i])).ToArray();
                s.shape = (BrushShape)GUILayout.Toolbar((int)s.shape, contents, EditorStyles.miniButton);
            }
            using (new EditorGUI.DisabledScope(s.shape != BrushShape.Circle))
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(new GUIContent("Taille", "Rayon en arêtes de grille. Raccourcis: [ ] ou Ctrl + molette"), GUILayout.Width(52));
                s.size = EditorGUILayout.Slider(s.size, WFCPaintSettings.MinSize, WFCPaintSettings.MaxSize);
            }
            if (EditorGUI.EndChangeCheck())
                s.Changed();
        }

        public static void ToolOptions(WFCPaintSettings s, GoldbergPolyhedron planet, WFCPlanetPainting painting, bool compact)
        {
            int floors = planet != null ? planet.Floors : 1;
            EditorGUI.BeginChangeCheck();
            switch (s.tool)
            {
                case PaintTool.Relief:
                    s.reliefOp = (ReliefOp)GUILayout.Toolbar((int)s.reliefOp, ReliefOpLabels, EditorStyles.miniButton);
                    if (s.reliefOp == ReliefOp.Paint)
                        HeightButtons(s, floors, HasHoles(planet));
                    ForceToggle(s);
                    break;

                case PaintTool.Stamp:
                    StampSummary(s, planet, compact);
                    s.stampFitRelief = EditorGUILayout.ToggleLeft(new GUIContent("Seulement là où il colle au relief",
                        "Une forêt ne recouvre pas la côte: les slots où aucun stamp choisi ne tient sont ignorés"), s.stampFitRelief);
                    using (new EditorGUI.DisabledScope(s.shape != BrushShape.Cell || s.stamps.Count != 1))
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label(new GUIContent("Rotation", "Forme Cellule + un seul stamp: sommet du slot qui reçoit la pointe"),
                            GUILayout.Width(60));
                        s.stampRotation = GUILayout.Toolbar(s.stampRotation + 1, RotationLabels, EditorStyles.miniButton) - 1;
                    }
                    break;

                case PaintTool.Zone:
                    ZonePicker(s, painting, compact);
                    break;

                case PaintTool.Lock:
                    EditorGUILayout.LabelField("Les stamps peints ne bougent plus (régénération, retouches). Shift = dégeler.",
                        EditorStyles.wordWrappedMiniLabel);
                    break;

                case PaintTool.Reroll:
                    EditorGUILayout.LabelField("Chaque passage retire au sort les stamps sous le pinceau, en respectant la peinture.",
                        EditorStyles.wordWrappedMiniLabel);
                    break;

                case PaintTool.Erase:
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        s.eraseRelief = GUILayout.Toggle(s.eraseRelief, "Relief", EditorStyles.miniButtonLeft);
                        s.eraseStamps = GUILayout.Toggle(s.eraseStamps, "Stamps", EditorStyles.miniButtonMid);
                        s.eraseZones = GUILayout.Toggle(s.eraseZones, "Zones", EditorStyles.miniButtonMid);
                        s.eraseLocks = GUILayout.Toggle(s.eraseLocks, "Gel", EditorStyles.miniButtonRight);
                    }
                    break;
            }
            if (EditorGUI.EndChangeCheck())
                s.Changed();
        }

        private static void ForceToggle(WFCPaintSettings s)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Règle", GUILayout.Width(52));
                int mode = GUILayout.Toolbar(s.force ? 0 : 1, new[]
                {
                    new GUIContent("Imposer", "Contrainte dure: le WFC doit poser exactement cette hauteur"),
                    new GUIContent("Guider", "Contrainte souple: le WFC est poussé vers cette hauteur, il peut s'en écarter")
                }, EditorStyles.miniButton);
                s.force = mode == 0;
            }
        }

        public static void HeightButtons(WFCPaintSettings s, int floors, bool holes = false)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                var previous = GUI.backgroundColor;
                for (int h = 0; h <= floors + 1; h++)
                {
                    GUI.backgroundColor = HeightColor(h, floors);
                    string label = HeightLabel(h, floors, holes);
                    if (h > 0 && h <= floors && floors > 1)
                        label = "É" + h;
                    bool on = s.height == h;
                    if (GUILayout.Toggle(on, new GUIContent(label, HeightLabel(h, floors, holes) + $" (touche {h})"), EditorStyles.miniButton) && !on)
                        s.height = h;
                }
                GUI.backgroundColor = previous;
            }
            s.height = Mathf.Clamp(s.height, 0, floors + 1);
        }

        private static void StampSummary(WFCPaintSettings s, GoldbergPolyhedron planet, bool compact)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (s.stamps.Count == 0)
                {
                    EditorGUILayout.HelpBox("Choisis un ou plusieurs stamps dans la palette (Ctrl + clic sur la planète = pipette).", MessageType.None);
                }
                else
                {
                    foreach (var element in s.stamps.Take(compact ? 5 : 10))
                    {
                        var rect = GUILayoutUtility.GetRect(36, 36, GUILayout.Width(36), GUILayout.Height(36));
                        Thumbnail(rect, element);
                        GUI.Label(rect, new GUIContent("", PrettyName(element)));
                    }
                    if (s.stamps.Count > (compact ? 5 : 10))
                        GUILayout.Label($"+{s.stamps.Count - (compact ? 5 : 10)}", EditorStyles.miniLabel);
                }
                GUILayout.FlexibleSpace();
                if (compact && GUILayout.Button("Palette…", EditorStyles.miniButton, GUILayout.Width(60)))
                    WFCPaintWindow.Open();
            }
            if (s.stamps.Count > 1)
                EditorGUILayout.LabelField("Plusieurs stamps: le WFC choisit parmi eux, slot par slot.", EditorStyles.miniLabel);
        }

        private static void ZonePicker(WFCPaintSettings s, WFCPlanetPainting painting, bool compact)
        {
            var zones = AllZones();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (zones.Count == 0)
                {
                    GUILayout.Label("Aucune zone.", EditorStyles.miniLabel);
                }
                else
                {
                    int index = Mathf.Max(0, zones.IndexOf(s.zone));
                    if (s.zone == null)
                        s.zone = zones[0];
                    index = EditorGUILayout.Popup(index, zones.Select(z => z.name.Replace("Zone_", "")).ToArray());
                    s.zone = zones[index];
                    var rect = GUILayoutUtility.GetRect(18, 18, GUILayout.Width(18));
                    EditorGUI.DrawRect(rect, s.zone.color);
                }
                if (GUILayout.Button(compact ? "Zones…" : "Nouvelle", EditorStyles.miniButton, GUILayout.Width(60)))
                {
                    if (compact)
                        WFCPaintWindow.Open();
                    else
                        s.zone = CreateZone("Zone_Nouvelle", null);
                }
            }
            if (s.zone != null)
                EditorGUILayout.LabelField(ZoneSummary(s.zone), EditorStyles.wordWrappedMiniLabel);
        }

        public static string ZoneSummary(WFCPaintZone zone)
        {
            var rules = zone.rules?.Where(r => r != null && r.element != null).ToList() ?? new List<WFCPaintZone.Rule>();
            if (rules.Count == 0)
                return "Zone sans règle: édite-la dans la fenêtre Planet Painter.";
            string Names(WFCPaintZone.RuleMode mode) => string.Join(", ", rules.Where(r => r.mode == mode).Select(r => PrettyName(r.element)));
            var parts = new List<string>();
            if (rules.Any(r => r.mode == WFCPaintZone.RuleMode.Favor))
                parts.Add("+ " + Names(WFCPaintZone.RuleMode.Favor));
            if (rules.Any(r => r.mode == WFCPaintZone.RuleMode.Avoid))
                parts.Add("~ " + Names(WFCPaintZone.RuleMode.Avoid));
            if (rules.Any(r => r.mode == WFCPaintZone.RuleMode.Forbid))
                parts.Add("✕ " + Names(WFCPaintZone.RuleMode.Forbid));
            return string.Join("   ", parts);
        }

        public static void SolveControls(WFCPaintSettings s)
        {
            EditorGUI.BeginChangeCheck();
            using (new EditorGUILayout.HorizontalScope())
            {
                s.liveSolve = GUILayout.Toggle(s.liveSolve, new GUIContent("Résoudre en direct", "Sinon au relâchement de la souris"),
                    EditorStyles.miniButton);
                GUILayout.Label(new GUIContent("Marge", "Couronnes de slots re-résolues autour du trait"), GUILayout.Width(42));
                s.margin = EditorGUILayout.IntSlider(s.margin, 1, 8);
            }
            if (EditorGUI.EndChangeCheck())
                s.Changed();
        }

        public static void Status(GoldbergPolyhedron planet)
        {
            if (!WFCPaintOps.HasReport)
                return;
            var style = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
            if (WFCPaintOps.LastReport.conflicts > 0 || WFCPaintOps.LastReport.relaxed > 0)
                style.normal.textColor = new Color(1f, 0.55f, 0.4f);
            GUILayout.Label(WFCPaintOps.LastReport.ToString(), style);
        }

        #endregion

        #region Calques, actions

        public static void LayerControls(WFCPaintSettings s, GoldbergPolyhedron planet, WFCPlanetPainting painting)
        {
            (WFCPlanetPainting.Layer layer, string label, Color color)[] layers =
            {
                (WFCPlanetPainting.Layer.Relief, "Relief", HeightColor(1, 2)),
                (WFCPlanetPainting.Layer.Stamps, "Stamps", WFCPaintVisuals.StampColor),
                (WFCPlanetPainting.Layer.Zones, "Zones", new Color(0.3f, 0.8f, 0.4f)),
                (WFCPlanetPainting.Layer.Locks, "Gel", WFCPaintVisuals.LockColor)
            };
            foreach (var (layer, label, color) in layers)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    var rect = GUILayoutUtility.GetRect(10, 16, GUILayout.Width(10));
                    EditorGUI.DrawRect(new Rect(rect.x, rect.y + 3, 10, 10), color);
                    EditorGUI.BeginChangeCheck();
                    bool show = GUILayout.Toggle(s.Shows(layer), new GUIContent(label, "Afficher le calque"), GUILayout.Width(80));
                    if (EditorGUI.EndChangeCheck())
                    {
                        switch (layer)
                        {
                            case WFCPlanetPainting.Layer.Relief: s.showRelief = show; break;
                            case WFCPlanetPainting.Layer.Stamps: s.showStamps = show; break;
                            case WFCPlanetPainting.Layer.Zones: s.showZones = show; break;
                            default: s.showLocks = show; break;
                        }
                        s.Changed();
                    }
                    int count = painting != null ? painting.Count(layer) : 0;
                    GUILayout.Label(count.ToString(), EditorStyles.miniLabel, GUILayout.Width(40));
                    GUILayout.FlexibleSpace();
                    using (new EditorGUI.DisabledScope(count == 0 || planet == null))
                        if (GUILayout.Button(layer == WFCPlanetPainting.Layer.Locks ? "Dégeler" : "Effacer", EditorStyles.miniButton, GUILayout.Width(64)))
                            WFCPaintOps.ClearLayer(planet, painting, layer);
                }
            }

            EditorGUI.BeginChangeCheck();
            using (new EditorGUILayout.HorizontalScope())
            {
                s.showConflicts = GUILayout.Toggle(s.showConflicts, new GUIContent("Conflits", "Slots où le WFC n'a pas trouvé de solution"),
                    EditorStyles.miniButtonLeft);
                s.xray = GUILayout.Toggle(s.xray, new GUIContent("Voir à travers", "La peinture reste visible derrière le relief"),
                    EditorStyles.miniButtonRight);
            }
            s.opacity = EditorGUILayout.Slider("Opacité", s.opacity, 0.1f, 1f);
            if (EditorGUI.EndChangeCheck())
                s.Changed();
        }

        public static void Actions(GoldbergPolyhedron planet, WFCPlanetPainting painting)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("Régénérer", "Relance tout le WFC (même graine) en gardant la peinture et le gel")))
                    WFCPaintOps.Regenerate(planet, painting);
                if (GUILayout.Button(new GUIContent("Nouvelle graine", "Nouvelle graine, la peinture et le gel restent")))
                {
                    var so = new SerializedObject(planet);
                    so.FindProperty("seed").intValue = Random.Range(0, 100000);
                    so.ApplyModifiedProperties();
                    WFCPaintOps.Regenerate(planet, painting);
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(painting == null))
                {
                    if (GUILayout.Button(new GUIContent("Tout geler", "Fige toute la planète: utile avant de changer des règles")))
                        WFCPaintOps.LockAll(planet, painting);
                    if (GUILayout.Button(new GUIContent("Tout effacer", "Efface tous les calques de peinture")) &&
                        EditorUtility.DisplayDialog("WFC Planet Painter", "Effacer toute la peinture de cette planète ?", "Effacer", "Annuler"))
                    {
                        foreach (WFCPlanetPainting.Layer layer in System.Enum.GetValues(typeof(WFCPlanetPainting.Layer)))
                            if (painting.Count(layer) > 0)
                                WFCPaintOps.ClearLayer(planet, painting, layer);
                    }
                }
            }
        }

        #endregion

        #region Palette, zones

        // stamps de la planete (theme), sinon ceux de la WFCDatabase sans theme
        public static List<WFCElementData> PlanetElements(GoldbergPolyhedron planet)
        {
            try
            {
                var theme = planet != null ? planet.ActiveTheme : null;
                var source = theme != null ? theme.elements : WFCDatabase.Instance.UnthemedElements();
                return (source ?? new List<WFCElementData>()).Where(e => e != null).Distinct().ToList();
            }
            catch (System.InvalidOperationException)
            {
                return new List<WFCElementData>();
            }
        }

        // grille de vignettes. clic = choisir, Ctrl/Shift + clic = ajouter / retirer
        public static void StampPalette(WFCPaintSettings s, List<WFCElementData> elements, float width)
        {
            const float cell = 72f;
            int columns = Mathf.Max(1, Mathf.FloorToInt((width - 8f) / (cell + 4f)));
            int rows = Mathf.CeilToInt(elements.Count / (float)columns);
            var area = GUILayoutUtility.GetRect(width - 8f, rows * (cell + 18f));
            var labelStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.UpperLeft, clipping = TextClipping.Clip };
            for (int i = 0; i < elements.Count; i++)
            {
                var element = elements[i];
                var rect = new Rect(area.x + (i % columns) * (cell + 4f), area.y + (i / columns) * (cell + 18f), cell, cell);
                bool selected = s.stamps.Contains(element);
                if (selected)
                    EditorGUI.DrawRect(new Rect(rect.x - 2, rect.y - 2, rect.width + 4, rect.height + 4), WFCPaintVisuals.StampColor);
                Thumbnail(rect, element);
                GUI.Label(new Rect(rect.x - 2, rect.yMax, cell + 4, 16), new GUIContent(PrettyName(element), PrettyName(element)), labelStyle);
                var corners = $"{element.heightApex}{element.heightCornerA}{element.heightCornerB}";
                GUI.Label(new Rect(rect.x + 2, rect.y + 1, cell, 14), corners, EditorStyles.whiteMiniLabel);

                var evt = Event.current;
                if (evt.type == EventType.MouseDown && evt.button == 0 && rect.Contains(evt.mousePosition))
                {
                    if (evt.control || evt.command || evt.shift)
                    {
                        if (!s.stamps.Remove(element))
                            s.stamps.Add(element);
                    }
                    else
                    {
                        s.stamps = new List<WFCElementData> { element };
                    }
                    if (evt.clickCount == 2)
                        EditorGUIUtility.PingObject(element);
                    s.tool = PaintTool.Stamp;
                    s.Changed();
                    evt.Use();
                }
            }
        }

        public static List<WFCPaintZone> AllZones() => AssetDatabase.FindAssets("t:" + nameof(WFCPaintZone))
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<WFCPaintZone>)
            .Where(z => z != null)
            .OrderBy(z => z.name)
            .ToList();

        public static WFCPaintZone CreateZone(string name, IEnumerable<WFCElementData> favored)
        {
            if (!AssetDatabase.IsValidFolder(ZoneFolder))
                AssetDatabase.CreateFolder("Assets/_WFC/WFCContent", "Zones");
            var zone = ScriptableObject.CreateInstance<WFCPaintZone>();
            zone.color = Color.HSVToRGB(Random.value, 0.65f, 0.9f);
            foreach (var element in favored ?? Enumerable.Empty<WFCElementData>())
                zone.SetRule(element, WFCPaintZone.RuleMode.Favor);
            var path = AssetDatabase.GenerateUniqueAssetPath($"{ZoneFolder}/{name}.asset");
            AssetDatabase.CreateAsset(zone, path);
            AssetDatabase.SaveAssets();
            return zone;
        }

        public static void Thumbnail(Rect rect, WFCElementData element)
        {
            EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.25f));
            if (element == null)
                return;
            Texture texture = null;
            if (element.Prefab != null)
                texture = AssetPreview.GetAssetPreview(element.Prefab) ?? AssetPreview.GetMiniThumbnail(element.Prefab);
            if (texture == null)
                texture = AssetPreview.GetMiniThumbnail(element);
            if (texture != null)
                GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit);
        }

        #endregion
    }
}
