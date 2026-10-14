using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using WFCContent.WFC;
using WFCContent.World;

namespace Editor
{
    // import auto des stamps exportes par BlenderTools/wfc_stamp_exporter.py
    // FBX dans Modules/ avec les custom props wfc_* -> prefab (copie de MODULE_FULL) + SO_WFC_* + ajout a la Database
    // wfc_theme (optionnel) = planete du stamp -> ajoute au Theme_<id> (cree si besoin)
    // les FBX sans props wfc_* (anciens modules) ne sont pas touches
    public class WFCStampImporter : AssetPostprocessor
    {
        private const string ModulesFolder = "Assets/_WFC/WFCContent/Mesh/Modules/";
        private const string PrefabsFolder = "Assets/_WFC/WFCContent/Mesh/Prefabs/";
        private const string DataFolder = "Assets/_WFC/WFCContent/Data/";
        // un theme par planete (wfc_theme): Theme_<id>.asset
        private const string ThemesFolder = "Assets/_WFC/WFCContent/Themes/";
        // meme repere que les stamps exportes (pointe a l'origine), sa cage FFD est le vrai triangle
        private const string TemplatePrefab = PrefabsFolder + "MODULE_FULL.prefab";
        private const string ModulePrefix = "MODULE_";
        // les stamps sont colores par vertex color (herbe, terre, eau...)
        private const string VertexColorShader = "Assets/_WFC/WFCContent/SG_VertexColor.shadergraph";
        private const string DecorSuffix = "_DECOR";

        private struct StampInfo
        {
            public int apex, cornerA, cornerB;
            public WFCEdge edgeApexA, edgeAB, edgeBApex;
            public float weight;
            public bool allowRotation;
            public string theme; // vide = pas de theme (ancien comportement)
            public bool peak, stackable;
            public float floorHeight; // 0 = pas donnee (stamp exporte avant les etages)
            public float seaDepth; // planete a gouffres: profondeur du vide en hauteurs de cage (0 = mer au ras du sol)
            public string exclude; // modules interdits comme voisins (MODULE_*, separes par des virgules)
            public float core; // planete creuse: rayon du noyau en fraction du rayon (0 = planete pleine)
        }

        private static readonly Dictionary<string, StampInfo> pending = new();

        private static bool IsModule(string path) =>
            path.StartsWith(ModulesFolder) && path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase);

        // nouveau FBX: memes reglages d'import que les modules existants
        private void OnPreprocessModel()
        {
            if (!IsModule(assetPath) || !assetImporter.importSettingsMissing)
                return;

            var importer = (ModelImporter)assetImporter;
            importer.globalScale = 10f;
            importer.isReadable = true; // FreeFormDeformer lit/ecrit les vertices
        }

        private void OnPostprocessGameObjectWithUserProperties(GameObject go, string[] names, object[] values)
        {
            if (!IsModule(assetPath) || Array.IndexOf(names, "wfc_apex") < 0)
                return;

            // sans wfc_peak (stamp exporte avant les etages): un coin a 2 etait toujours une montagne
            bool hasPeak = Array.IndexOf(names, "wfc_peak") >= 0;
            var info = new StampInfo { weight = 1f, allowRotation = true, stackable = true };
            for (int i = 0; i < names.Length; i++)
            {
                switch (names[i])
                {
                    case "wfc_apex": info.apex = Mathf.RoundToInt(ToFloat(values[i])); break;
                    case "wfc_a": info.cornerA = Mathf.RoundToInt(ToFloat(values[i])); break;
                    case "wfc_b": info.cornerB = Mathf.RoundToInt(ToFloat(values[i])); break;
                    case "wfc_edge_oa": info.edgeApexA = (WFCEdge)Mathf.RoundToInt(ToFloat(values[i])); break;
                    case "wfc_edge_ab": info.edgeAB = (WFCEdge)Mathf.RoundToInt(ToFloat(values[i])); break;
                    case "wfc_edge_bo": info.edgeBApex = (WFCEdge)Mathf.RoundToInt(ToFloat(values[i])); break;
                    case "wfc_weight": info.weight = ToFloat(values[i]); break;
                    case "wfc_rotation": info.allowRotation = ToFloat(values[i]) != 0f; break;
                    case "wfc_theme": info.theme = Convert.ToString(values[i], CultureInfo.InvariantCulture)?.Trim(); break;
                    case "wfc_peak": info.peak = ToFloat(values[i]) != 0f; break;
                    case "wfc_stack": info.stackable = ToFloat(values[i]) != 0f; break;
                    case "wfc_floor": info.floorHeight = ToFloat(values[i]); break;
                    case "wfc_sea_depth": info.seaDepth = ToFloat(values[i]); break;
                    case "wfc_exclude": info.exclude = Convert.ToString(values[i], CultureInfo.InvariantCulture); break;
                    case "wfc_core": info.core = ToFloat(values[i]); break;
                }
            }
            if (!hasPeak)
                info.peak = Mathf.Max(info.apex, Mathf.Max(info.cornerA, info.cornerB)) >= 2;
            pending[assetPath] = info;
        }

        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (imported.Any(pending.ContainsKey))
                // pas de creation d'assets pendant l'import, on differe
                EditorApplication.delayCall += ProcessPending;
        }

        [MenuItem("Tools/WFC/Reimport Stamps")]
        public static void ReimportAll()
        {
            var fbxPaths = AssetDatabase.FindAssets("t:Model", new[] { ModulesFolder.TrimEnd('/') })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(IsModule);
            foreach (var path in fbxPaths)
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            ProcessPending();
        }

        private static void ProcessPending()
        {
            if (pending.Count == 0)
                return;

            var stamps = pending.ToList();
            pending.Clear();

            foreach (var (path, info) in stamps)
                Configure(path, info);
            // regles venues de Blender: apres coup, quand tous les SO existent
            foreach (var (path, info) in stamps)
                ApplyExclusions(path, info);

            AssetDatabase.SaveAssets();
            WFCElementData.NotifyRulesChanged();
        }

        private static string DataPath(string moduleName) =>
            DataFolder + "SO_WFC_" + (moduleName.StartsWith(ModulePrefix) ? moduleName.Substring(ModulePrefix.Length) : moduleName) + ".asset";

        // wfc_exclude: voisins interdits (regle "Exclu"). on ajoute seulement: les regles posees a la main restent
        private static void ApplyExclusions(string fbxPath, StampInfo info)
        {
            if (string.IsNullOrWhiteSpace(info.exclude))
                return;
            var data = AssetDatabase.LoadAssetAtPath<WFCElementData>(DataPath(Path.GetFileNameWithoutExtension(fbxPath)));
            if (data == null)
                return;

            var rules = data.Data;
            rules.excludedElements ??= new List<WFCElementData>();
            bool changed = false;
            foreach (var name in info.exclude.Split(',').Select(n => n.Trim()).Where(n => n.Length > 0))
            {
                var other = AssetDatabase.LoadAssetAtPath<WFCElementData>(DataPath(name));
                if (other == null)
                    Debug.LogWarning($"[WFC] {data.name}: voisin interdit {name} introuvable (stamp pas encore importe ?)", data);
                else if (!rules.excludedElements.Contains(other))
                {
                    rules.excludedElements.Add(other);
                    changed = true;
                }
            }
            if (!changed)
                return;
            data.Data = rules;
            EditorUtility.SetDirty(data);
        }

        private static void Configure(string fbxPath, StampInfo info)
        {
            string name = Path.GetFileNameWithoutExtension(fbxPath);
            string suffix = name.StartsWith(ModulePrefix) ? name.Substring(ModulePrefix.Length) : name;

            // sol = mesh principal (collision), decor = arbres/maisons/eau, exporte a part (suffixe _DECOR).
            // une piece au-dessus du vide (rochers qui flottent) n'a que du decor: pas de sol
            var meshes = AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<Mesh>().ToList();
            var mesh = meshes.FirstOrDefault(m => !m.name.Contains(DecorSuffix) && m.vertexCount > 0);
            var decorMesh = meshes.FirstOrDefault(m => m.name.Contains(DecorSuffix));
            if (mesh == null && decorMesh == null)
            {
                Debug.LogError($"[WFC] Pas de mesh dans {fbxPath}");
                return;
            }

            string dataPath = DataFolder + "SO_WFC_" + suffix + ".asset";
            var data = AssetDatabase.LoadAssetAtPath<WFCElementData>(dataPath);
            if (data == null)
            {
                data = ScriptableObject.CreateInstance<WFCElementData>();
                AssetDatabase.CreateAsset(data, dataPath);
            }

            string prefabPath = PrefabsFolder + name + ".prefab";
            bool created = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null;
            if (created && !AssetDatabase.CopyAsset(TemplatePrefab, prefabPath))
            {
                Debug.LogError($"[WFC] Impossible de copier {TemplatePrefab} vers {prefabPath}");
                return;
            }

            var template = AssetDatabase.LoadAssetAtPath<GameObject>(TemplatePrefab);
            var templateFfd = template.GetComponent<FreeFormDeformerElement>();
            var templateRender = GroundFilter(template).transform;
            var material = AssetDatabase.LoadAllAssetsAtPath(VertexColorShader).OfType<Material>().FirstOrDefault();
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                root.name = name;
                var meshFilter = GroundFilter(root);
                meshFilter.sharedMesh = mesh;
                // le mesh exporte est dans le repere de MODULE_FULL: meme transform que lui (sinon il sort de la cage)
                CopyLocalTransform(templateRender, meshFilter.transform);
                if (material != null)
                    meshFilter.GetComponent<MeshRenderer>().sharedMaterial = material;
                root.GetComponent<FreeFormDeformerElement>().Anchors = new List<Vector3>(templateFfd.Anchors);

                var decor = root.transform.Find(GoldbergPolyhedron.DecorChildName);
                if (decorMesh != null)
                {
                    if (decor == null)
                    {
                        decor = new GameObject(GoldbergPolyhedron.DecorChildName, typeof(MeshFilter), typeof(MeshRenderer)).transform;
                        decor.SetParent(root.transform, false);
                    }
                    CopyLocalTransform(templateRender, decor);
                    decor.GetComponent<MeshFilter>().sharedMesh = decorMesh;
                    if (material != null)
                        decor.GetComponent<MeshRenderer>().sharedMaterial = material;
                }
                else if (decor != null)
                {
                    UnityEngine.Object.DestroyImmediate(decor.gameObject);
                }

                var element = new SerializedObject(root.GetComponent<WFCElement>());
                element.FindProperty("data").objectReferenceValue = data;
                element.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            // les regles de voisinage deja posees dans le SO sont conservees
            data.Prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            data.heightApex = info.apex;
            data.heightCornerA = info.cornerA;
            data.heightCornerB = info.cornerB;
            data.edgeApexA = info.edgeApexA;
            data.edgeAB = info.edgeAB;
            data.edgeBApex = info.edgeBApex;
            data.weight = info.weight;
            data.allowRotation = info.allowRotation;
            data.peak = info.peak;
            data.stackable = info.stackable;
            EditorUtility.SetDirty(data);

            var database = WFCDatabase.Instance;
            database.elements ??= new List<WFCElementData>();
            if (!database.elements.Contains(data))
            {
                database.elements.Add(data);
                EditorUtility.SetDirty(database);
            }

            // la database reste le registre de tous les stamps, le theme dit sur quelle planete il apparait
            var theme = string.IsNullOrEmpty(info.theme) ? null : GetOrCreateTheme(database, info.theme);
            if (theme != null)
            {
                AssignToTheme(theme, data, database);
                // hauteur d'un etage = celle des pentes modelisees dans Blender: les stamps empiles se raccordent aux pentes
                if (info.floorHeight > 0f && !Mathf.Approximately(theme.floorHeight, info.floorHeight))
                {
                    theme.floorHeight = info.floorHeight;
                    EditorUtility.SetDirty(theme);
                }
                // gouffres: le vide (sphere du niveau 0) descend sous le pied des falaises modelisees dans Blender
                if (info.seaDepth > 0f && !Mathf.Approximately(theme.seaDepth, info.seaDepth))
                {
                    theme.seaDepth = info.seaDepth;
                    EditorUtility.SetDirty(theme);
                }
                // planete creuse: la sphere du niveau 0 devient le noyau, vu par les trous de la coquille
                if (info.core > 0f && !Mathf.Approximately(theme.coreRadius, info.core))
                {
                    theme.coreRadius = info.core;
                    EditorUtility.SetDirty(theme);
                }
            }

            Debug.Log($"[WFC] Stamp {(created ? "cree" : "mis a jour")}: {name} (pointe {info.apex}, A {info.cornerA}, B {info.cornerB}, " +
                      $"aretes {info.edgeApexA}/{info.edgeAB}/{info.edgeBApex}, poids {info.weight}, {(info.peak ? "montagne, " : "")}" +
                      $"theme {(theme != null ? theme.name : "aucun")})");
            CheckCage(data.Prefab);
        }

        // theme de la database, sinon un theme du projet avec cet id (cree a la main), sinon l'asset Theme_<id>,
        // sinon on le cree (avec la piece vide pour la mer). jamais mis en defaultTheme: c'est un choix a la main
        private static WFCPlanetTheme GetOrCreateTheme(WFCDatabase database, string id)
        {
            var theme = database.FindTheme(id)
                        ?? AllThemes(database).FirstOrDefault(t => string.Equals(t.id?.Trim(), id, StringComparison.OrdinalIgnoreCase));
            if (theme == null)
            {
                string themePath = ThemesFolder + "Theme_" + id + ".asset";
                theme = AssetDatabase.LoadAssetAtPath<WFCPlanetTheme>(themePath);
                if (theme == null)
                {
                    if (!AssetDatabase.IsValidFolder(ThemesFolder.TrimEnd('/')))
                        AssetDatabase.CreateFolder("Assets/_WFC/WFCContent", "Themes");
                    theme = ScriptableObject.CreateInstance<WFCPlanetTheme>();
                    theme.id = id;
                    // pieces sans prefab = vides (SO_WFC_EMPTY), la mer de toutes les planetes
                    theme.elements.AddRange(database.elements.Where(e => e != null && e.Prefab == null).Distinct());
                    AssetDatabase.CreateAsset(theme, themePath);
                    Debug.Log($"[WFC] Theme cree: {themePath} ({theme.elements.Count} piece(s) vide(s)), " +
                              "a mettre sur une planete (GoldbergPolyhedron.theme)", theme);
                }
            }

            database.themes ??= new List<WFCPlanetTheme>();
            if (!database.themes.Contains(theme))
            {
                database.themes.Add(theme);
                EditorUtility.SetDirty(database);
            }
            return theme;
        }

        // un stamp = une seule planete: ajoute a son theme, retire des autres
        private static void AssignToTheme(WFCPlanetTheme theme, WFCElementData data, WFCDatabase database)
        {
            theme.elements ??= new List<WFCElementData>();
            if (!theme.elements.Contains(data))
            {
                theme.elements.Add(data);
                EditorUtility.SetDirty(theme);
            }

            foreach (var other in AllThemes(database).Where(t => t != theme))
                if (other.elements != null && other.elements.RemoveAll(e => e == data) > 0)
                    EditorUtility.SetDirty(other);
        }

        // tous les themes du projet: une planete peut utiliser un theme que la database ne liste pas
        private static IEnumerable<WFCPlanetTheme> AllThemes(WFCDatabase database) =>
            AssetDatabase.FindAssets("t:" + nameof(WFCPlanetTheme))
                .Select(guid => AssetDatabase.LoadAssetAtPath<WFCPlanetTheme>(AssetDatabase.GUIDToAssetPath(guid)))
                .Concat(database.themes ?? new List<WFCPlanetTheme>())
                .Where(t => t != null)
                .Distinct();

        [MenuItem("Tools/WFC/Validate Stamps")]
        public static void ValidateAll()
        {
            int bad = WFCDatabase.Instance.Elements
                .Where(e => e != null && e.Prefab != null)
                .Distinct()
                .Count(e => !CheckCage(e.Prefab));
            Debug.Log($"[WFC] Validation terminee: {bad} stamp(s) hors de leur cage");
        }

        private static MeshFilter GroundFilter(GameObject root) =>
            root.GetComponentsInChildren<MeshFilter>(true).First(mf => mf.name != GoldbergPolyhedron.DecorChildName);

        private static void CopyLocalTransform(Transform from, Transform to)
        {
            to.localPosition = from.localPosition;
            to.localRotation = from.localRotation;
            to.localScale = from.localScale;
        }

        // un vertex hors de la cage FFD finit hors de son triangle une fois deforme = superposition avec les voisins
        private static bool CheckCage(GameObject prefab)
        {
            var ffd = prefab.GetComponent<FreeFormDeformerElement>();
            if (ffd == null || ffd.Anchors.Count != 6)
                return true;
            return prefab.GetComponentsInChildren<MeshFilter>(true)
                .Where(mf => mf.sharedMesh != null)
                .Aggregate(true, (ok, mf) => CheckCage(prefab, ffd, mf) && ok);
        }

        private static bool CheckCage(GameObject prefab, FreeFormDeformerElement ffd, MeshFilter meshFilter)
        {
            // meme repere que FreeFormDeformer: origine = ancre 0, axes s/t dans le triangle, u = hauteur
            var cage = ffd.GetRelativeAnchors();
            Vector3 origin = cage[0], axisS = cage[1] - origin, axisT = cage[2] - origin, axisU = cage[3] - origin;
            float det = Vector3.Dot(axisS, Vector3.Cross(axisT, axisU));
            Vector3 crossTU = Vector3.Cross(axisT, axisU), crossUS = Vector3.Cross(axisU, axisS);

            float minS = float.MaxValue, minT = float.MaxValue, maxSum = float.MinValue;
            foreach (var v in meshFilter.sharedMesh.vertices)
            {
                Vector3 p = meshFilter.transform.TransformPoint(v) - origin;
                float s = Vector3.Dot(p, crossTU) / det;
                float t = Vector3.Dot(p, crossUS) / det;
                minS = Mathf.Min(minS, s);
                minT = Mathf.Min(minT, t);
                maxSum = Mathf.Max(maxSum, s + t);
            }

            // le sol doit rester dans son triangle (sinon superposition), le decor (feuillages) peut deborder un peu sur les voisins
            float tolerance = meshFilter.name == GoldbergPolyhedron.DecorChildName ? 0.2f : 0.01f;
            bool ok = minS >= -tolerance && minT >= -tolerance && maxSum <= 1f + tolerance;
            if (!ok)
                Debug.LogWarning($"[WFC] {prefab.name}/{meshFilter.name} deborde de sa cage FFD (s min {minS:F3}, t min {minT:F3}, s+t max {maxSum:F3}): " +
                                 "il va se superposer aux voisins", prefab);
            return ok;
        }

        private static float ToFloat(object value) => value switch
        {
            bool b => b ? 1f : 0f,
            string s => float.Parse(s, CultureInfo.InvariantCulture),
            _ => Convert.ToSingle(value, CultureInfo.InvariantCulture)
        };
    }
}
