using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using WFCContent.WFC;
using WFCContent.World;

namespace Editor
{
    public class WFCRulesWindow : EditorWindow
    {
        private enum Rule { Neutral, Included, Excluded }

        private static readonly string[] RuleLabels = { "—", "Autorisé", "Exclu" };
        private static readonly Color IncludedColor = new Color(0.35f, 0.8f, 0.4f);
        private static readonly Color ExcludedColor = new Color(0.9f, 0.35f, 0.3f);

        private const float ListWidth = 210f;
        private const float RowThumb = 40f;

        [SerializeField] private WFCElementData selected;
        [SerializeField] private bool showMatrix = true;

        private Vector2 listScroll;
        private Vector2 rulesScroll;
        private WFCDatabase database;
        private WFCPlanetTheme themeFilter; // null = tous les stamps

        [MenuItem("Tools/WFC/Rules Editor")]
        public static void Open()
        {
            var window = GetWindow<WFCRulesWindow>("WFC Rules");
            window.minSize = new Vector2(620, 360);
        }

        private void OnEnable()
        {
            Undo.undoRedoPerformed += OnUndoRedo;
            WFCLivePreview.Regenerated += Repaint;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            WFCLivePreview.Regenerated -= Repaint;
        }

        private void OnUndoRedo()
        {
            // OnValidate declenche deja la preview si une regle a bouge, ici on rafraichit juste l'UI
            Repaint();
        }

        private void OnSelectionChange()
        {
            if (Selection.activeObject is WFCElementData data)
            {
                selected = data;
                Repaint();
            }
        }

        private void OnGUI()
        {
            database = LoadDatabase();
            DrawToolbar();

            if (database == null)
            {
                EditorGUILayout.HelpBox("Aucune WFCDatabase trouvée dans Resources/Singletons.", MessageType.Error);
                return;
            }

            var source = themeFilter != null ? themeFilter.elements : database.Elements;
            var elements = (source ?? new List<WFCElementData>()).Where(e => e != null).Distinct().ToList();
            if (elements.Count == 0)
            {
                EditorGUILayout.HelpBox(themeFilter != null ? "Ce thème est vide." : "La WFCDatabase est vide.", MessageType.Info);
                return;
            }

            if (selected == null || !elements.Contains(selected))
                selected = elements[0];

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawPieceList(elements);
                DrawRules(elements);
            }

            if (AssetPreview.IsLoadingAssetPreviews())
                Repaint();
        }

        #region Toolbar

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                WFCLivePreview.Enabled = GUILayout.Toggle(WFCLivePreview.Enabled, "Live preview", EditorStyles.toolbarButton, GUILayout.Width(90));

                if (GUILayout.Button("Rebuild", EditorStyles.toolbarButton, GUILayout.Width(60)))
                    WFCLivePreview.RegenerateAll();

                if (database != null)
                    DrawThemeFilter();

                var planet = FindObjectsByType<GoldbergPolyhedron>().FirstOrDefault();
                if (planet != null)
                {
                    var so = new SerializedObject(planet);
                    var seedProp = so.FindProperty("seed");

                    GUILayout.Space(8);
                    GUILayout.Label("Seed", GUILayout.Width(32));
                    EditorGUI.BeginChangeCheck();
                    int newSeed = EditorGUILayout.DelayedIntField(seedProp.intValue, EditorStyles.toolbarTextField, GUILayout.Width(70));
                    if (GUILayout.Button("Random", EditorStyles.toolbarButton, GUILayout.Width(55)))
                        newSeed = Random.Range(0, 100000);
                    if (EditorGUI.EndChangeCheck() || newSeed != seedProp.intValue)
                    {
                        seedProp.intValue = newSeed;
                        so.ApplyModifiedProperties();
                        WFCLivePreview.RegenerateAll();
                    }

                    GUILayout.FlexibleSpace();

                    if (planet.LastSlotCount > 0)
                    {
                        var style = new GUIStyle(EditorStyles.miniLabel);
                        if (planet.LastConflictCount > 0)
                            style.normal.textColor = ExcludedColor;
                        GUILayout.Label($"{planet.LastSlotCount} slots · {planet.LastConflictCount} conflit(s)", style);
                    }
                }
                else
                {
                    GUILayout.FlexibleSpace();
                    GUILayout.Label("Pas de GoldbergPolyhedron dans la scène", EditorStyles.miniLabel);
                }
            }
        }

        // filtre par planete: la liste et la matrice ne montrent que les stamps du theme
        private void DrawThemeFilter()
        {
            var themes = (database.Themes ?? new List<WFCPlanetTheme>()).Where(t => t != null).Distinct().ToList();
            var labels = new[] { "Tous les stamps" }
                .Concat(themes.Select(t => string.IsNullOrEmpty(t.id) ? t.name : t.id))
                .ToArray();

            GUILayout.Space(8);
            int index = EditorGUILayout.Popup(themes.IndexOf(themeFilter) + 1, labels, EditorStyles.toolbarPopup, GUILayout.Width(120));
            themeFilter = index > 0 ? themes[index - 1] : null;
        }

        #endregion

        #region Piece list

        private void DrawPieceList(List<WFCElementData> elements)
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(ListWidth)))
            {
                GUILayout.Label("Pièces", EditorStyles.boldLabel);
                listScroll = EditorGUILayout.BeginScrollView(listScroll);

                foreach (var el in elements)
                {
                    var rect = EditorGUILayout.GetControlRect(false, RowThumb + 4);
                    bool isSelected = el == selected;

                    if (isSelected)
                        EditorGUI.DrawRect(rect, new Color(0.24f, 0.48f, 0.9f, 0.35f));

                    var thumbRect = new Rect(rect.x + 2, rect.y + 2, RowThumb, RowThumb);
                    DrawThumbnail(thumbRect, el);

                    var labelRect = new Rect(thumbRect.xMax + 6, rect.y, rect.width - RowThumb - 8, rect.height);
                    GUI.Label(labelRect, PrettyName(el), isSelected ? EditorStyles.boldLabel : EditorStyles.label);

                    DrawRuleBadge(new Rect(rect.xMax - 34, rect.y + rect.height * 0.5f - 8, 32, 16), el);

                    if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
                    {
                        selected = el;
                        if (Event.current.clickCount == 2)
                            EditorGUIUtility.PingObject(el);
                        Event.current.Use();
                        Repaint();
                    }
                }

                EditorGUILayout.EndScrollView();
            }
        }

        // petit resume: nb autorises / nb exclus
        private static void DrawRuleBadge(Rect rect, WFCElementData el)
        {
            int inc = el.Data.includedElements?.Count(e => e != null) ?? 0;
            int exc = el.Data.excludedElements?.Count(e => e != null) ?? 0;
            if (inc == 0 && exc == 0)
                return;

            var style = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight, richText = true };
            GUI.Label(rect, $"<color=#55cc66>{inc}</color>/<color=#ee6655>{exc}</color>", style);
        }

        #endregion

        #region Rules

        private void DrawRules(List<WFCElementData> elements)
        {
            using (new EditorGUILayout.VerticalScope())
            {
                DrawSelectedHeader(elements);

                rulesScroll = EditorGUILayout.BeginScrollView(rulesScroll);

                DrawCorners();

                EditorGUILayout.Space(8);
                GUILayout.Label("Voisins (règles par pièce)", EditorStyles.boldLabel);
                var whitelist = selected.HasWhitelist();
                EditorGUILayout.HelpBox(
                    whitelist
                        ? "Whitelist active : seules les pièces « Autorisé » peuvent être voisines."
                        : "Pas de whitelist : toutes les pièces sont acceptées sauf celles « Exclu ».",
                    MessageType.None);
                // piece partagee (piece vide = mer de toutes les planetes): les pieces cachees par le filtre sont concernees aussi
                if (themeFilter != null && database.Themes.Any(t => t != null && t != themeFilter && t.elements != null && t.elements.Contains(selected)))
                    EditorGUILayout.HelpBox("Pièce partagée avec d'autres thèmes : ses règles valent sur toutes ces planètes " +
                                            "(une whitelist bloque aussi les pièces des thèmes cachés).", MessageType.Warning);

                foreach (var other in elements)
                    DrawRuleRow(other);

                EditorGUILayout.Space(8);
                showMatrix = EditorGUILayout.Foldout(showMatrix, "Matrice des règles par pièce (hors coins)", true);
                if (showMatrix)
                    DrawMatrix(elements);

                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawCorners()
        {
            EditorGUILayout.Space(6);
            GUILayout.Label("Coins (hauteurs)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Hauteur aux 3 sommets du triangle = étage (0 mer, 1 terre, 2, 3... plateaux). Deux voisins doivent avoir la même " +
                "hauteur sur les sommets qu'ils partagent. Sur une planète à plusieurs étages, une pièce sans mer est aussi posée " +
                "plus haut (hauteurs + décalage). Pointe = sommet d'origine du mesh (centre de cellule sans rotation). " +
                "Pas de prefab = pièce vide.",
                MessageType.None);

            var so = new SerializedObject(selected);
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawHeightField(so, "heightApex", "Pointe");
                DrawHeightField(so, "heightCornerA", "Coin A");
                DrawHeightField(so, "heightCornerB", "Coin B");
            }

            GUILayout.Label("Arêtes (rivière...) : deux voisins doivent avoir la même chose sur l'arête partagée", EditorStyles.miniLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawEdgeField(so, "edgeApexA", "Pointe → A");
                DrawEdgeField(so, "edgeAB", "A → B (bord)");
                DrawEdgeField(so, "edgeBApex", "B → Pointe");
            }
            EditorGUILayout.PropertyField(so.FindProperty("allowRotation"), new GUIContent("Rotation autorisée"));
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PropertyField(so.FindProperty("peak"), new GUIContent("Montagne",
                    "Le coin le plus haut est un pic, posé sur le dernier étage de la planète"));
                EditorGUILayout.PropertyField(so.FindProperty("stackable"), new GUIContent("Empilable",
                    "Peut aussi être posée sur les étages du dessus (sauf si elle touche la mer)"));
            }
            EditorGUILayout.PropertyField(so.FindProperty("weight"), new GUIContent("Poids"));
            EditorGUILayout.PropertyField(so.FindProperty("Prefab"));
            so.ApplyModifiedProperties(); // declenche OnValidate -> live preview
        }

        private static void DrawHeightField(SerializedObject so, string property, string label)
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(80)))
            {
                GUILayout.Label(label, EditorStyles.miniLabel);
                var prop = so.FindProperty(property);
                prop.intValue = EditorGUILayout.IntField(prop.intValue, GUILayout.Width(70));
            }
        }

        private static void DrawEdgeField(SerializedObject so, string property, string label)
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(100)))
            {
                GUILayout.Label(label, EditorStyles.miniLabel);
                EditorGUILayout.PropertyField(so.FindProperty(property), GUIContent.none, GUILayout.Width(90));
            }
        }

        private void DrawSelectedHeader(List<WFCElementData> elements)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                var thumbRect = GUILayoutUtility.GetRect(64, 64, GUILayout.Width(64), GUILayout.Height(64));
                DrawThumbnail(thumbRect, selected);

                using (new EditorGUILayout.VerticalScope())
                {
                    GUILayout.Label(PrettyName(selected), EditorStyles.largeLabel);
                    using (new EditorGUI.DisabledScope(true))
                        EditorGUILayout.ObjectField(selected, typeof(WFCElementData), false);

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("Tout neutre", EditorStyles.miniButtonLeft))
                            SetAll(Rule.Neutral, elements);
                        if (GUILayout.Button("Tout autoriser", EditorStyles.miniButtonMid))
                            SetAll(Rule.Included, elements);
                        if (GUILayout.Button("Tout exclure", EditorStyles.miniButtonRight))
                            SetAll(Rule.Excluded, elements);
                    }
                }
            }
        }

        private void DrawRuleRow(WFCElementData other)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                var thumbRect = GUILayoutUtility.GetRect(RowThumb, RowThumb, GUILayout.Width(RowThumb), GUILayout.Height(RowThumb));
                DrawThumbnail(thumbRect, other);

                using (new EditorGUILayout.VerticalScope())
                {
                    string label = PrettyName(other) + (other == selected ? "  (elle-même)" : "");
                    GUILayout.Label(label, EditorStyles.boldLabel);

                    var (ok, reason) = Explain(selected, other);
                    var style = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = ok ? IncludedColor : ExcludedColor } };
                    GUILayout.Label(ok ? "compatible" : "bloqué : " + reason, style);
                }

                GUILayout.FlexibleSpace();

                var current = GetRule(selected, other);
                var prevBg = GUI.backgroundColor;
                GUI.backgroundColor = current switch
                {
                    Rule.Included => IncludedColor,
                    Rule.Excluded => ExcludedColor,
                    _ => prevBg
                };
                var next = (Rule)GUILayout.Toolbar((int)current, RuleLabels, GUILayout.Width(210), GUILayout.Height(22));
                GUI.backgroundColor = prevBg;

                if (next != current)
                    SetRule(selected, other, next);
            }
        }

        private void DrawMatrix(List<WFCElementData> elements)
        {
            const float cell = 22f;
            const float header = 110f;

            // lignes = piece, colonnes = voisin. clic = selectionne la ligne
            var total = GUILayoutUtility.GetRect(header + cell * elements.Count, header + cell * elements.Count);
            var origin = new Vector2(total.x + header, total.y + header);

            var vertStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleLeft };
            for (int c = 0; c < elements.Count; c++)
            {
                var pivot = new Vector2(origin.x + c * cell + cell * 0.5f, origin.y - 4);
                var matrix = GUI.matrix;
                GUIUtility.RotateAroundPivot(-90, pivot);
                GUI.Label(new Rect(pivot.x, pivot.y - cell * 0.5f, header, cell), PrettyName(elements[c]), vertStyle);
                GUI.matrix = matrix;
            }

            for (int r = 0; r < elements.Count; r++)
            {
                var rowStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight };
                if (elements[r] == selected)
                    rowStyle.fontStyle = FontStyle.Bold;
                GUI.Label(new Rect(total.x, origin.y + r * cell, header - 4, cell), PrettyName(elements[r]), rowStyle);

                for (int c = 0; c < elements.Count; c++)
                {
                    var rect = new Rect(origin.x + c * cell + 1, origin.y + r * cell + 1, cell - 2, cell - 2);
                    bool ok = WFCElementData.AreCompatible(elements[r], elements[c]);
                    EditorGUI.DrawRect(rect, ok ? IncludedColor * 0.85f : ExcludedColor * 0.85f);

                    var rule = GetRule(elements[r], elements[c]);
                    if (rule != Rule.Neutral)
                        GUI.Label(rect, rule == Rule.Included ? "+" : "-", new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter });

                    if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
                    {
                        selected = elements[r];
                        Event.current.Use();
                        Repaint();
                    }
                }
            }

            EditorGUILayout.LabelField("Vert = peuvent être voisines · Rouge = interdit · +/- = règle posée par la pièce de la ligne", EditorStyles.miniLabel);
        }

        #endregion

        #region Rules data

        private static Rule GetRule(WFCElementData owner, WFCElementData other)
        {
            if (owner.Data.excludedElements != null && owner.Data.excludedElements.Contains(other))
                return Rule.Excluded;
            if (owner.Data.includedElements != null && owner.Data.includedElements.Contains(other))
                return Rule.Included;
            return Rule.Neutral;
        }

        private static void SetRule(WFCElementData owner, WFCElementData other, Rule rule)
        {
            Undo.RecordObject(owner, "Change WFC Rule");
            ApplyRule(owner, other, rule);
            EditorUtility.SetDirty(owner);
            WFCElementData.NotifyRulesChanged();
        }

        // seulement les pieces affichees: avec un filtre de theme, les regles vers les autres planetes ne bougent pas
        private void SetAll(Rule rule, List<WFCElementData> elements)
        {
            Undo.RecordObject(selected, "Change WFC Rules");
            foreach (var other in elements)
                ApplyRule(selected, other, rule);
            EditorUtility.SetDirty(selected);
            WFCElementData.NotifyRulesChanged();
        }

        private static void ApplyRule(WFCElementData owner, WFCElementData other, Rule rule)
        {
            // struct: on modifie une copie puis on la reassigne
            var data = owner.Data;
            data.includedElements ??= new List<WFCElementData>();
            data.excludedElements ??= new List<WFCElementData>();

            data.includedElements.RemoveAll(e => e == other || e == null);
            data.excludedElements.RemoveAll(e => e == other || e == null);

            if (rule == Rule.Included)
                data.includedElements.Add(other);
            else if (rule == Rule.Excluded)
                data.excludedElements.Add(other);

            owner.Data = data;
        }

        // pourquoi deux pieces ne peuvent pas etre voisines
        private static (bool ok, string reason) Explain(WFCElementData a, WFCElementData b)
        {
            var reasons = new List<string>();
            AddReasons(a, b, reasons);
            if (a != b)
                AddReasons(b, a, reasons);
            return (reasons.Count == 0, string.Join(", ", reasons));
        }

        private static void AddReasons(WFCElementData owner, WFCElementData other, List<string> reasons)
        {
            if (owner.Data.excludedElements != null && owner.Data.excludedElements.Contains(other))
                reasons.Add($"exclu par {PrettyName(owner)}");
            else if (owner.HasWhitelist() && !owner.Data.includedElements.Contains(other))
                reasons.Add($"hors whitelist de {PrettyName(owner)}");
        }

        #endregion

        #region Utils

        private static WFCDatabase LoadDatabase()
        {
            try
            {
                return WFCDatabase.Instance;
            }
            catch (System.InvalidOperationException)
            {
                return null;
            }
        }

        private static string PrettyName(WFCElementData el) => el.name.Replace("SO_WFC_", "");

        private static void DrawThumbnail(Rect rect, WFCElementData el)
        {
            Texture tex = null;
            if (el.Prefab != null)
                tex = AssetPreview.GetAssetPreview(el.Prefab) ?? AssetPreview.GetMiniThumbnail(el.Prefab);
            if (tex == null)
                tex = AssetPreview.GetMiniThumbnail(el);

            EditorGUI.DrawRect(rect, new Color(0, 0, 0, 0.2f));
            if (tex != null)
                GUI.DrawTexture(rect, tex, ScaleMode.ScaleToFit);
        }

        #endregion
    }
}
