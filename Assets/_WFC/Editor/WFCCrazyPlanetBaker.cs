using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Physics.Authoring;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using WFCContent.WFC;
using WFCContent.World;

namespace Editor
{
    // remplace les planetes Earth et Volcanus de Crazy Planet par des planetes WFC: genere la planete (memes reglages que la
    // SampleScene du projet WFC), la fige en meshes (rendu par material + collider du sol), construit le prefab de jeu
    // (PlanetDataAuthoring + PhysicsShapeAuthoring sur Landscape) et le visuel du lobby, puis les pose dans les scenes.
    // batch: Unity -batchmode -quit -projectPath ... -executeMethod Editor.WFCCrazyPlanetBaker.BakeAllBatch
    public static class WFCCrazyPlanetBaker
    {
        public class PlanetJob
        {
            public string Name;              // Earth, Volcanus
            public int PlanetId;             // EPlanetID
            public string ThemePath;
            public string ScenePath;
            public string LobbyInstanceName; // instance dans PF_UI_PlanetSelection
            public int Density = 4;
            public int Seed = 67;
            public float FallbackRadius = 100f;
            public float FallbackRunDuration = 300f;
            // racines des decors qui suivaient le relief de l'ancienne planete: desactivees (pas detruites)
            public string[] DisableRoots = new string[0];
        }

        public static readonly PlanetJob[] Jobs =
        {
            new PlanetJob
            {
                Name = "Earth", PlanetId = 3, ThemePath = "Assets/_WFC/WFCContent/Themes/Theme_Earth.asset",
                ScenePath = "Assets/_Scenes/SC_Main/SC_Earth.unity", LobbyInstanceName = "PF_PlanetEarth",
                FallbackRunDuration = 300f, DisableRoots = new[] { "Avoidance Container" },
            },
            new PlanetJob
            {
                Name = "Volcanus", PlanetId = 2, ThemePath = "Assets/_WFC/WFCContent/Themes/Theme_Volcanus.asset",
                ScenePath = "Assets/_Scenes/SC_Main/SC_Volcanus.unity", LobbyInstanceName = "PF_Planet_Volcanus",
                FallbackRunDuration = 600f, DisableRoots = new[] { "Enviro", "HazardZone" },
            },
        };

        private const string OutputRoot = "Assets/_Prefabs/Planets";
        private const string SelectionPrefab = "Assets/_Prefabs/UI/Lobby/PF_UI_PlanetSelection.prefab";
        private const string PlanetMaterialPath = "Assets/_WFC/WFCContent/SG_VertexColor.shadergraph";
        private const string DefaultStampPath = "Assets/_WFC/WFCContent/Mesh/UnityHugeSheetAsMyAss.prefab";
        private const float GenerationRadius = 5f;
        private const uint Landscape = 1u << 5, Raycast = 1u << 6;
        private static readonly string[] SurfaceRoots = { "PF_PlayerStart", "PF_EnemiesSpawner", "Spawner", "PF_Ressource", "PF_Mill" };

        public static readonly List<string> Report = new();

        [MenuItem("Tools/WFC/Bake Planets For Crazy Planet (Earth + Volcanus)")]
        public static void BakeAllMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            BakeAll();
            Debug.Log(string.Join("\n", Report));
        }

        public static void BakeAllBatch()
        {
            try
            {
                BakeAll();
                File.WriteAllText("Temp/WFCCrazyPlanetBaker.txt", string.Join("\n", Report));
                Debug.Log("[WFCBaker] DONE\n" + string.Join("\n", Report));
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                File.WriteAllText("Temp/WFCCrazyPlanetBaker.txt", "FAILED\n" + e + "\n" + string.Join("\n", Report));
                EditorApplication.Exit(1);
            }
        }

        // lobby seul, avec les visuels deja cuits (sans regenerer ni toucher aux scenes)
        public static void SwapLobbyBatch()
        {
            Report.Clear();
            foreach (var job in Jobs)
            {
                var visual = AssetDatabase.LoadAssetAtPath<GameObject>($"{OutputRoot}/WFC_{job.Name}/PF_WFC_Planet_{job.Name}_Visual.prefab");
                if (visual != null)
                    SwapLobbyVisual(job, visual);
            }
            AssetDatabase.SaveAssets();
            File.WriteAllText("Temp/WFCCrazyPlanetBaker.txt", string.Join("\n", Report));
        }

        public static void BakeAll()
        {
            Report.Clear();
            foreach (var job in Jobs)
                Bake(job);
            AssetDatabase.SaveAssets();
        }

        public static void Bake(PlanetJob job)
        {
            // 1) ancienne planete: rayon, centre, duree de run
            var scene = EditorSceneManager.OpenScene(job.ScenePath, OpenSceneMode.Single);
            var old = FindPlanet(job);
            float targetRadius = old != null ? old.WorldRadius : job.FallbackRadius;
            Vector3 center = old != null ? (Vector3)old.WorldCenter : Vector3.zero;
            float runDuration = old != null ? new SerializedObject(old).FindProperty("_runDuration").floatValue : job.FallbackRunDuration;
            var oldRoot = old != null ? OutermostRoot(old.gameObject) : null;
            Report.Add($"[{job.Name}] old planet '{(oldRoot != null ? oldRoot.name : "none")}' radius {targetRadius:0.##} center {center} run {runDuration}s");

            // 2) generation dans une scene vide (la scene de jeu reste intacte jusqu'a la pose)
            var (render, collider, materials) = Generate(job, targetRadius / GenerationRadius);

            string folder = $"{OutputRoot}/WFC_{job.Name}";
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder(OutputRoot, "WFC_" + job.Name);
            render = SaveMesh(render, $"{folder}/M_WFC_{job.Name}_Render.asset");
            collider = SaveMesh(collider, $"{folder}/M_WFC_{job.Name}_Collider.asset");

            var visual = BuildVisualPrefab(render, materials, $"{folder}/PF_WFC_Planet_{job.Name}_Visual.prefab");
            var gameplay = BuildGameplayPrefab(job, render, collider, materials, targetRadius, runDuration, $"{folder}/PF_WFC_Planet_{job.Name}.prefab");
            Report.Add($"[{job.Name}] theme {Path.GetFileNameWithoutExtension(job.ThemePath)} density {job.Density} seed {job.Seed} radius {targetRadius:0.##}: " +
                       $"render {render.triangles.Length / 3} tris / {render.vertexCount} verts / {render.subMeshCount} materials, " +
                       $"collider {collider.triangles.Length / 3} tris");

            // 3) scene de jeu: nouvelle planete a la place de l'ancienne, objets de surface reposes sur le nouveau sol
            scene = EditorSceneManager.OpenScene(job.ScenePath, OpenSceneMode.Single);
            old = FindPlanet(job);
            oldRoot = old != null ? OutermostRoot(old.gameObject) : null;
            int sibling = oldRoot != null ? oldRoot.transform.GetSiblingIndex() : 0;
            if (oldRoot != null)
            {
                Report.Add($"[{job.Name}] removed old planet '{oldRoot.name}'");
                Object.DestroyImmediate(oldRoot);
            }
            var planet = (GameObject)PrefabUtility.InstantiatePrefab(gameplay, scene);
            planet.transform.position = center;
            planet.transform.SetSiblingIndex(sibling);

            foreach (var rootName in job.DisableRoots)
            {
                var go = scene.GetRootGameObjects().FirstOrDefault(g => g.name == rootName);
                if (go == null) continue;
                go.SetActive(false);
                Report.Add($"[{job.Name}] disabled '{rootName}' ({go.transform.childCount} children, placed for the old relief)");
            }
            Resnap(job, scene, planet, collider, center, targetRadius);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            // 4) lobby: visuel WFC dans l'instance existante (references du prefab conservees), anciens renderers coupes
            SwapLobbyVisual(job, visual);
        }

        private static PlanetDataAuthoring FindPlanet(PlanetJob job) =>
            Object.FindObjectsByType<PlanetDataAuthoring>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(p => (int)p.PlanetID == job.PlanetId);

        // ---------------------------------------------------------------- generation

        private static (Mesh render, Mesh collider, Material[] materials) Generate(PlanetJob job, float scale)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // singleton du solveur: present dans la scene (sinon il est cree avec DontDestroyOnLoad, interdit hors Play)
            new GameObject("WFC Manager").AddComponent<WaveFunctionCollapseManager>();

            var go = new GameObject("WFC " + job.Name, typeof(MeshFilter), typeof(MeshRenderer), typeof(SphereCollider));
            var planetMaterial = AssetDatabase.LoadAllAssetsAtPath(PlanetMaterialPath).OfType<Material>().FirstOrDefault();
            go.GetComponent<MeshRenderer>().sharedMaterial = planetMaterial;
            var planet = go.AddComponent<GoldbergPolyhedron>();
            var so = new SerializedObject(planet);
            so.FindProperty("density").intValue = job.Density;
            so.FindProperty("radius").floatValue = GenerationRadius;
            so.FindProperty("seed").intValue = job.Seed;
            so.FindProperty("theme").objectReferenceValue = AssetDatabase.LoadAssetAtPath<WFCPlanetTheme>(job.ThemePath);
            so.FindProperty("defaultStamp").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(DefaultStampPath);
            so.ApplyModifiedPropertiesWithoutUndo();
            planet.Generate();
            Report.Add($"[{job.Name}] generated {planet.LastSlotCount} slots, {planet.LastConflictCount} conflicts");

            // rendu: tous les meshes, un sous-mesh par material. collider: sol des stamps (hors Decor) + niveau 0 (mer/lave)
            var toPlanet = Matrix4x4.Scale(Vector3.one * scale) * go.transform.worldToLocalMatrix;
            var byMaterial = new Dictionary<Material, List<CombineInstance>>();
            var ground = new List<CombineInstance>();
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
            {
                var mr = mf.GetComponent<MeshRenderer>();
                if (mf.sharedMesh == null || mr == null || !mr.enabled || !mf.gameObject.activeInHierarchy)
                    continue;
                var matrix = toPlanet * mf.transform.localToWorldMatrix;
                var mats = mr.sharedMaterials;
                bool decor = UnderDecor(mf.transform, go.transform);
                for (int s = 0; s < mf.sharedMesh.subMeshCount; s++)
                {
                    var mat = mats.Length > 0 ? mats[Mathf.Min(s, mats.Length - 1)] : null;
                    if (mat == null) mat = planetMaterial;
                    if (!byMaterial.TryGetValue(mat, out var list))
                        byMaterial[mat] = list = new List<CombineInstance>();
                    var part = new CombineInstance { mesh = mf.sharedMesh, subMeshIndex = s, transform = matrix };
                    list.Add(part);
                    if (!decor)
                        ground.Add(part);
                }
            }

            var materials = byMaterial.Keys.ToArray();
            var parts = new List<CombineInstance>();
            foreach (var mat in materials)
            {
                var part = NewMesh("part");
                part.CombineMeshes(byMaterial[mat].ToArray(), true, true);
                parts.Add(new CombineInstance { mesh = part, transform = Matrix4x4.identity });
            }
            var render = NewMesh($"WFC_{job.Name}_Render");
            render.CombineMeshes(parts.ToArray(), false, true);
            render.RecalculateBounds();

            var collider = NewMesh($"WFC_{job.Name}_Collider");
            collider.CombineMeshes(ground.ToArray(), true, true);
            var positionsOnly = NewMesh(collider.name);
            positionsOnly.SetVertices(collider.vertices);
            positionsOnly.SetTriangles(collider.triangles, 0);
            positionsOnly.RecalculateNormals();
            positionsOnly.RecalculateBounds();
            Object.DestroyImmediate(collider);

            foreach (var p in parts) Object.DestroyImmediate(p.mesh);
            Object.DestroyImmediate(go);
            return (render, positionsOnly, materials);
        }

        private static bool UnderDecor(Transform t, Transform root)
        {
            for (; t != null && t != root; t = t.parent)
                if (t.name == GoldbergPolyhedron.DecorChildName)
                    return true;
            return false;
        }

        private static Mesh NewMesh(string name) => new Mesh { name = name, indexFormat = IndexFormat.UInt32 };

        private static Mesh SaveMesh(Mesh mesh, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                // meme asset (meme GUID): les prefabs et scenes qui le referencent suivent
                EditorUtility.CopySerialized(mesh, existing);
                existing.name = Path.GetFileNameWithoutExtension(path);
                Object.DestroyImmediate(mesh);
                EditorUtility.SetDirty(existing);
                return existing;
            }
            mesh.name = Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        // ---------------------------------------------------------------- prefabs

        private static GameObject BuildVisualPrefab(Mesh render, Material[] materials, string path)
        {
            var go = new GameObject(Path.GetFileNameWithoutExtension(path));
            go.AddComponent<MeshFilter>().sharedMesh = render;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = materials;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }

        private static GameObject BuildGameplayPrefab(PlanetJob job, Mesh render, Mesh collider, Material[] materials, float radius,
            float runDuration, string path)
        {
            var go = new GameObject(Path.GetFileNameWithoutExtension(path));
            GameObjectUtility.SetStaticEditorFlags(go, (StaticEditorFlags)127);
            go.AddComponent<MeshFilter>().sharedMesh = render;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = materials;

            var data = go.AddComponent<PlanetDataAuthoring>();
            var so = new SerializedObject(data);
            so.FindProperty("_planetID").intValue = job.PlanetId;
            so.FindProperty("_autoCalculate").boolValue = false;
            so.FindProperty("_planetRenderer").objectReferenceValue = mr;
            so.FindProperty("_manualRadius").floatValue = radius;
            so.FindProperty("_manualCenterOffset").vector3Value = Vector3.zero;
            so.FindProperty("_runDuration").floatValue = runDuration;
            so.ApplyModifiedPropertiesWithoutUndo();

            // meme reglage que l'ancienne Volcanus: Mesh, appartient a Landscape, collisionne avec Raycast
            var shape = go.AddComponent<PhysicsShapeAuthoring>();
            shape.SetMesh(collider);
            shape.BelongsTo = new PhysicsCategoryTags { Value = Landscape };
            shape.CollidesWith = new PhysicsCategoryTags { Value = Raycast };

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }

        // ---------------------------------------------------------------- scene

        private static GameObject OutermostRoot(GameObject go)
        {
            var root = PrefabUtility.GetOutermostPrefabInstanceRoot(go);
            return root != null ? root : go;
        }

        // objets poses sur l'ancienne surface: rayon vers le centre sur le nouveau collider, meme hauteur au-dessus du sol
        private static void Resnap(PlanetJob job, Scene scene, GameObject planet, Mesh collider, Vector3 center, float radius)
        {
            var probe = new GameObject("WFC Resnap Probe");
            SceneManager.MoveGameObjectToScene(probe, scene);
            probe.transform.position = center;
            var meshCollider = probe.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = collider;
            Physics.SyncTransforms();

            var targets = new List<Transform>();
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root == planet || root == probe || !root.activeSelf) continue;
                if (root.name == "Resource Sources")
                    foreach (Transform child in root.transform) targets.Add(child);
                else if (SurfaceRoots.Any(n => root.name.StartsWith(n)))
                    targets.Add(root.transform);
            }

            int moved = 0;
            var skipped = new List<string>();
            foreach (var t in targets)
            {
                Vector3 offset = t.position - center;
                if (offset.magnitude < radius * 0.5f) { skipped.Add(t.name); continue; }
                Vector3 dir = offset.normalized;
                float height = Mathf.Max(0f, offset.magnitude - radius);
                var ray = new Ray(center + dir * radius * 2f, -dir);
                if (!meshCollider.Raycast(ray, out var hit, radius * 2f)) { skipped.Add(t.name); continue; }
                t.position = hit.point + dir * height;
                moved++;
            }
            Object.DestroyImmediate(probe);
            Report.Add($"[{job.Name}] re-snapped {moved} objects onto the WFC surface" +
                       (skipped.Count > 0 ? $"; left as is (not on the surface or no hit): {string.Join(", ", skipped)}" : ""));
        }

        // ---------------------------------------------------------------- lobby

        private static void SwapLobbyVisual(PlanetJob job, GameObject visual)
        {
            var contents = PrefabUtility.LoadPrefabContents(SelectionPrefab);
            try
            {
                var target = contents.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == job.LobbyInstanceName);
                if (target == null)
                {
                    Report.Add($"[{job.Name}] lobby: '{job.LobbyInstanceName}' not found in PF_UI_PlanetSelection, untouched");
                    return;
                }
                const string childName = "WFC Visual";
                var previous = target.Find(childName);
                if (previous != null) Object.DestroyImmediate(previous.gameObject);

                var oldRenderers = target.GetComponentsInChildren<Renderer>(true);
                Bounds? oldBounds = null;
                foreach (var r in oldRenderers)
                {
                    var mf = r.GetComponent<MeshFilter>();
                    if (r is not MeshRenderer || mf == null || mf.sharedMesh == null) continue;
                    var b = WorldBounds(mf);
                    if (oldBounds == null) oldBounds = b;
                    else { var ob = oldBounds.Value; ob.Encapsulate(b); oldBounds = ob; }
                }

                var inst = (GameObject)PrefabUtility.InstantiatePrefab(visual, target);
                inst.name = childName;
                inst.layer = target.gameObject.layer;
                var mesh = inst.GetComponent<MeshFilter>().sharedMesh;
                inst.transform.localRotation = Quaternion.identity;
                inst.transform.localScale = Vector3.one;
                if (oldBounds != null)
                {
                    float wanted = oldBounds.Value.extents.magnitude;
                    float current = Vector3.Scale(mesh.bounds.extents, inst.transform.lossyScale).magnitude;
                    if (current > 1e-5f) inst.transform.localScale = Vector3.one * (wanted / current);
                    inst.transform.position = oldBounds.Value.center;
                }
                foreach (var r in oldRenderers)
                    r.enabled = false;
                PrefabUtility.SaveAsPrefabAsset(contents, SelectionPrefab);
                Report.Add($"[{job.Name}] lobby: '{job.LobbyInstanceName}' now shows the WFC planet ({oldRenderers.Length} old renderers disabled)");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        private static Bounds WorldBounds(MeshFilter mf)
        {
            var mb = mf.sharedMesh.bounds;
            var m = mf.transform.localToWorldMatrix;
            var b = new Bounds(m.MultiplyPoint3x4(mb.center), Vector3.zero);
            for (int i = 0; i < 8; i++)
            {
                var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                b.Encapsulate(m.MultiplyPoint3x4(corner));
            }
            return b;
        }
    }
}
