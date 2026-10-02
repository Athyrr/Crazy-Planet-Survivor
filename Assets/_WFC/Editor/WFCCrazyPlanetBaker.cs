using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Physics.Authoring;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using WFCContent.WFC;
using WFCContent.World;

namespace Editor
{
    // remplace les planetes Earth et Volcanus de Crazy Planet par des planetes WFC: genere la planete (memes reglages que la
    // SampleScene du projet WFC), la fige en meshes et construit les prefabs de jeu et du lobby, puis les pose dans les scenes.
    //  - sol (stamps) et mer/lave (niveau 0) separes, decoupes en morceaux pour le frustum culling (une entite par morceau)
    //  - distance au rivage cuite dans l'uv1 de la mer (ecume, eau peu profonde, croute de lave)
    //  - materials propres (WFC/Planet Land, Water, Lava, Atmosphere), crees une fois puis gardes (retouches conservees)
    //  - collider du sol separe (PhysicsShapeAuthoring Mesh sur Landscape), zones de lave (HazardZone) posees sur la vraie lave
    // re-cuire est sans risque: une scene qui contient deja la planete WFC n'est pas re-calee (seul le prefab change)
    // batch: Unity -batchmode -quit -projectPath ... -executeMethod Editor.WFCCrazyPlanetBaker.BakeAllBatch
    public static class WFCCrazyPlanetBaker
    {
        public enum SeaStyle { Water, Lava }

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
            public SeaStyle Sea;
            public bool LavaHazards;
            public Color RimColor, AtmoColor, SunsetColor, LowTint, ShadowTint, HazeColor;
            public float AtmoScale = 1.12f;
            public bool Clouds;
            public float CloudScale = 1.055f;
        }

        public static readonly PlanetJob[] Jobs =
        {
            new PlanetJob
            {
                Name = "Earth", PlanetId = 3, ThemePath = "Assets/_WFC/WFCContent/Themes/Theme_Earth.asset",
                ScenePath = "Assets/_Scenes/SC_Main/SC_Earth.unity", LobbyInstanceName = "PF_PlanetEarth",
                FallbackRunDuration = 300f, DisableRoots = new[] { "Avoidance Container" },
                Sea = SeaStyle.Water, LavaHazards = false, Clouds = true,
                RimColor = new Color(0.45f, 0.8f, 1.5f), AtmoColor = new Color(0.3f, 0.62f, 1.6f), SunsetColor = new Color(1.5f, 0.65f, 0.3f),
                LowTint = new Color(0.72f, 0.8f, 0.82f), ShadowTint = new Color(0.3f, 0.42f, 0.7f), HazeColor = new Color(0.45f, 0.65f, 0.95f, 0.35f),
            },
            new PlanetJob
            {
                Name = "Volcanus", PlanetId = 2, ThemePath = "Assets/_WFC/WFCContent/Themes/Theme_Volcanus.asset",
                ScenePath = "Assets/_Scenes/SC_Main/SC_Volcanus.unity", LobbyInstanceName = "PF_Planet_Volcanus",
                FallbackRunDuration = 600f, DisableRoots = new[] { "Enviro", "HazardZone" },
                Sea = SeaStyle.Lava, LavaHazards = true,
                RimColor = new Color(1.6f, 0.45f, 0.12f), AtmoColor = new Color(1.4f, 0.38f, 0.1f), SunsetColor = new Color(1.8f, 0.9f, 0.3f),
                LowTint = new Color(0.8f, 0.72f, 0.72f), ShadowTint = new Color(0.4f, 0.14f, 0.1f), HazeColor = new Color(0.35f, 0.1f, 0.04f, 0.3f),
                AtmoScale = 1.1f,
            },
        };

        private const string OutputRoot = "Assets/_Prefabs/Planets";
        private const string SelectionPrefab = "Assets/_Prefabs/UI/Lobby/PF_UI_PlanetSelection.prefab";
        private const string PlanetMaterialPath = "Assets/_WFC/WFCContent/SG_VertexColor.shadergraph";
        private const string DefaultStampPath = "Assets/_WFC/WFCContent/Mesh/UnityHugeSheetAsMyAss.prefab";
        private const string LavaHazardPrefab = "Assets/_Prefabs/World/PF_HazardZone_Lava.prefab";
        private const string HazardRootName = "WFC Lava Hazards";
        private const string AmbienceRootName = "WFC Ambience";
        private const float GenerationRadius = 5f;
        private const int ChunkCount = 40;
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
                Debug.Log("[WFCBaker] DONE\n" + string.Join("\n", Report));
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                Debug.Log("[WFCBaker] FAILED\n" + string.Join("\n", Report));
                EditorApplication.Exit(1);
            }
        }

        // lobby seul, avec les visuels deja cuits (sans regenerer ni toucher aux scenes)
        public static void SwapLobbyBatch()
        {
            Report.Clear();
            foreach (var job in Jobs)
            {
                var visual = AssetDatabase.LoadAssetAtPath<GameObject>($"{Folder(job)}/PF_WFC_Planet_{job.Name}_Visual.prefab");
                if (visual != null)
                    SwapLobbyVisual(job, visual);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[WFCBaker] DONE\n" + string.Join("\n", Report));
        }

        public static void BakeAll()
        {
            Report.Clear();
            foreach (var job in Jobs)
                Bake(job);
            AssetDatabase.SaveAssets();
        }

        private static string Folder(PlanetJob job) => $"{OutputRoot}/WFC_{job.Name}";

        public static void Bake(PlanetJob job)
        {
            string folder = Folder(job);
            string gameplayPath = $"{folder}/PF_WFC_Planet_{job.Name}.prefab";

            // 1) planete actuelle de la scene: rayon, centre, duree de run. deja la planete WFC = re-cuisson (pas de re-calage)
            var scene = EditorSceneManager.OpenScene(job.ScenePath, OpenSceneMode.Single);
            var old = FindPlanet(job);
            var oldRoot = old != null ? OutermostRoot(old.gameObject) : null;
            bool alreadyWfc = oldRoot != null && PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(oldRoot) == gameplayPath;
            float targetRadius = old != null ? old.WorldRadius : job.FallbackRadius;
            Vector3 center = old != null ? (Vector3)old.WorldCenter : Vector3.zero;
            float runDuration = old != null ? new SerializedObject(old).FindProperty("_runDuration").floatValue : job.FallbackRunDuration;
            Report.Add($"[{job.Name}] current planet '{(oldRoot != null ? oldRoot.name : "none")}'{(alreadyWfc ? " (WFC, re-bake)" : "")} " +
                       $"radius {targetRadius:0.##} center {center} run {runDuration}s");

            // 2) generation dans une scene vide (la scene de jeu reste intacte jusqu'a la pose)
            var baked = Generate(job, targetRadius / GenerationRadius);
            var shore = ComputeShore(baked.Sea, baked.LandGround, out var covered);
            // les meshes non sauvegardes sont decharges a l'ouverture d'une scene: on garde les sommets de la mer
            var seaVerts = baked.Sea.vertices;
            SeaRadius = seaVerts.Length > 0 ? seaVerts.Average(v => v.magnitude) : 0f;

            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder(OutputRoot, "WFC_" + job.Name);
            var materials = EnsureMaterials(job, folder, targetRadius);

            // morceaux pour le culling; lobby = un seul mesh (sol + mer en 2 sous-meshes)
            var dirs = FibonacciDirections(ChunkCount);
            var landChunks = Chunk(baked.Land, dirs, $"WFC_{job.Name}_Land");
            var seaChunks = Chunk(baked.Sea, dirs, $"WFC_{job.Name}_Sea");
            var lobby = Combine(new[] { baked.Land, baked.Sea }, $"WFC_{job.Name}_Lobby");
            var sphere = BuildSphere(48, 24);
            SaveChunks($"{folder}/M_WFC_{job.Name}_Chunks.asset", landChunks.Concat(seaChunks).Append(sphere).ToList());
            lobby = SaveMesh(lobby, $"{folder}/M_WFC_{job.Name}_Render.asset");
            var collider = SaveMesh(baked.Collider, $"{folder}/M_WFC_{job.Name}_Collider.asset");
            baked.Collider = collider;

            var visual = BuildVisualPrefab(job, lobby, sphere, materials, $"{folder}/PF_WFC_Planet_{job.Name}_Visual.prefab");
            var gameplay = BuildGameplayPrefab(job, landChunks, seaChunks, sphere, collider, materials, targetRadius, runDuration, gameplayPath);
            Report.Add($"[{job.Name}] theme {Path.GetFileNameWithoutExtension(job.ThemePath)} density {job.Density} seed {job.Seed}: " +
                       $"land {Tris(baked.Land)} tris in {landChunks.Count} chunks, sea {Tris(baked.Sea)} tris in {seaChunks.Count} chunks " +
                       $"({covered * 100f / Mathf.Max(1, baked.Sea.vertexCount):0}% of the sea under land), collider {Tris(collider)} tris");

            // 3) scene de jeu
            scene = EditorSceneManager.OpenScene(job.ScenePath, OpenSceneMode.Single);
            old = FindPlanet(job);
            oldRoot = old != null ? OutermostRoot(old.gameObject) : null;
            GameObject planet;
            if (alreadyWfc && oldRoot != null)
                planet = oldRoot; // instance du prefab: suit le prefab re-cuit
            else
            {
                int sibling = oldRoot != null ? oldRoot.transform.GetSiblingIndex() : 0;
                if (oldRoot != null)
                {
                    Report.Add($"[{job.Name}] removed old planet '{oldRoot.name}'");
                    Object.DestroyImmediate(oldRoot);
                }
                planet = (GameObject)PrefabUtility.InstantiatePrefab(gameplay, scene);
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
            }

            MovePlayerStartOnLand(job, scene, seaVerts, collider, shore, center, targetRadius);
            var hazards = job.LavaHazards ? PlaceLavaHazards(job, scene, seaVerts, shore, center) : new List<(Vector3 p, float r)>();
            BuildAmbience(job, scene, folder, center, hazards);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            // 4) lobby: visuel WFC dans l'instance existante (references du prefab conservees), anciens renderers coupes
            SwapLobbyVisual(job, visual);
        }

        private static int Tris(Mesh m) => Enumerable.Range(0, m.subMeshCount).Sum(s => (int)m.GetIndexCount(s)) / 3;

        private static PlanetDataAuthoring FindPlanet(PlanetJob job) =>
            Object.FindObjectsByType<PlanetDataAuthoring>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(p => (int)p.PlanetID == job.PlanetId);

        // ---------------------------------------------------------------- generation

        private class Baked
        {
            public Mesh Land;       // stamps (sol + decor), un sous-mesh par material d'origine
            public Material[] LandSourceMaterials;
            public Mesh LandGround; // stamps hors decor (pour savoir ce qui recouvre la mer)
            public Mesh Sea;        // niveau 0
            public Mesh Collider;   // sol + niveau 0
        }

        private static Baked Generate(PlanetJob job, float scale)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // singleton du solveur: present dans la scene (sinon il est cree avec DontDestroyOnLoad, interdit hors Play)
            new GameObject("WFC Manager").AddComponent<WaveFunctionCollapseManager>();

            var theme = AssetDatabase.LoadAssetAtPath<WFCPlanetTheme>(job.ThemePath);
            var go = new GameObject("WFC " + job.Name, typeof(MeshFilter), typeof(MeshRenderer), typeof(SphereCollider));
            var planetMaterial = AssetDatabase.LoadAllAssetsAtPath(PlanetMaterialPath).OfType<Material>().FirstOrDefault();
            go.GetComponent<MeshRenderer>().sharedMaterial = planetMaterial;
            var planet = go.AddComponent<GoldbergPolyhedron>();
            var so = new SerializedObject(planet);
            so.FindProperty("density").intValue = job.Density;
            so.FindProperty("radius").floatValue = GenerationRadius;
            so.FindProperty("seed").intValue = job.Seed;
            so.FindProperty("theme").objectReferenceValue = theme;
            so.FindProperty("defaultStamp").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(DefaultStampPath);
            so.ApplyModifiedPropertiesWithoutUndo();
            planet.Generate();
            Report.Add($"[{job.Name}] generated {planet.LastSlotCount} slots, {planet.LastConflictCount} conflicts");

            var toPlanet = Matrix4x4.Scale(Vector3.one * scale) * go.transform.worldToLocalMatrix;
            var byMaterial = new Dictionary<Material, List<CombineInstance>>();
            var ground = new List<CombineInstance>();
            CombineInstance? sea = null;
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
            {
                var mr = mf.GetComponent<MeshRenderer>();
                if (mf.sharedMesh == null || mr == null || !mr.enabled || !mf.gameObject.activeInHierarchy)
                    continue;
                var matrix = toPlanet * mf.transform.localToWorldMatrix;
                if (mf.transform == go.transform)
                {
                    sea = new CombineInstance { mesh = mf.sharedMesh, transform = matrix };
                    continue;
                }
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

            var baked = new Baked { LandSourceMaterials = byMaterial.Keys.ToArray() };
            var parts = new List<CombineInstance>();
            foreach (var mat in baked.LandSourceMaterials)
            {
                var part = NewMesh("part");
                part.CombineMeshes(byMaterial[mat].ToArray(), true, true);
                parts.Add(new CombineInstance { mesh = part, transform = Matrix4x4.identity });
            }
            baked.Land = NewMesh($"WFC_{job.Name}_Land");
            baked.Land.CombineMeshes(parts.ToArray(), false, true);
            foreach (var p in parts) Object.DestroyImmediate(p.mesh);

            // Earth: l'eau des rivieres (decor des stamps) a la couleur de la "mer" du theme: elle passe au bleu de l'eau
            if (job.Sea == SeaStyle.Water && theme != null)
                RecolorRivers(baked.Land, theme.seaColor, new Color(0.08f, 0.5f, 0.62f));

            baked.LandGround = NewMesh("ground");
            baked.LandGround.CombineMeshes(ground.ToArray(), true, true);

            baked.Sea = NewMesh($"WFC_{job.Name}_Sea");
            if (sea != null)
                baked.Sea.CombineMeshes(new[] { sea.Value }, true, true);

            var all = new List<CombineInstance>(ground);
            if (sea != null) all.Add(sea.Value);
            var colliderFull = NewMesh("collider");
            colliderFull.CombineMeshes(all.ToArray(), true, true);
            baked.Collider = NewMesh($"WFC_{job.Name}_Collider");
            baked.Collider.SetVertices(colliderFull.vertices);
            baked.Collider.SetTriangles(colliderFull.triangles, 0);
            baked.Collider.RecalculateNormals();
            baked.Collider.RecalculateBounds();
            Object.DestroyImmediate(colliderFull);

            Object.DestroyImmediate(go);
            return baked;
        }

        private static void RecolorRivers(Mesh mesh, Color key, Color water)
        {
            var colors = mesh.colors;
            if (colors == null || colors.Length == 0) return;
            int n = 0;
            for (int i = 0; i < colors.Length; i++)
            {
                var c = colors[i];
                if (Close(c, key) || Close(c, key.linear))
                {
                    colors[i] = new Color(water.r, water.g, water.b, c.a);
                    n++;
                }
            }
            mesh.colors = colors;
            // diagnostic: couleurs les plus frequentes (pour regler la cle)
            var top = colors.GroupBy(c => new Vector3Int(Mathf.RoundToInt(c.r * 20), Mathf.RoundToInt(c.g * 20), Mathf.RoundToInt(c.b * 20)))
                .OrderByDescending(g => g.Count()).Take(12).Select(g => $"({g.Key.x / 20f:0.00},{g.Key.y / 20f:0.00},{g.Key.z / 20f:0.00})x{g.Count()}");
            Report.Add($"  rivers recolored: {n} vertices; key {key} / {key.linear}; top colors {string.Join(" ", top)}");
        }

        private static bool Close(Color a, Color b) => Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) < 0.14f;

        private static bool UnderDecor(Transform t, Transform root)
        {
            for (; t != null && t != root; t = t.parent)
                if (t.name == GoldbergPolyhedron.DecorChildName)
                    return true;
            return false;
        }

        private static Mesh NewMesh(string name) => new Mesh { name = name, indexFormat = IndexFormat.UInt32 };

        // ---------------------------------------------------------------- rivage

        // distance (monde) de chaque sommet de la mer au rivage: sommets recouverts par le sol = 0, puis Dijkstra sur les aretes.
        // uv1 = (distance, recouvert)
        private static float[] ComputeShore(Mesh sea, Mesh landGround, out int coveredCount)
        {
            var verts = sea.vertices;
            var dist = new float[verts.Length];
            coveredCount = 0;
            if (verts.Length == 0) return dist;

            var probe = new GameObject("WFC Shore Probe");
            var mc = probe.AddComponent<MeshCollider>();
            mc.sharedMesh = landGround;
            Physics.SyncTransforms();
            float outer = verts.Max(v => v.magnitude) * 1.8f;

            var covered = new bool[verts.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                var dir = verts[i].normalized;
                var ray = new Ray(dir * outer, -dir);
                covered[i] = mc.Raycast(ray, out var hit, outer) && hit.point.magnitude > verts[i].magnitude + 0.25f;
                if (covered[i]) coveredCount++;
            }
            Object.DestroyImmediate(probe);

            var tris = sea.triangles;
            var adj = new List<int>[verts.Length];
            for (int i = 0; i < verts.Length; i++) adj[i] = new List<int>(6);
            for (int t = 0; t < tris.Length; t += 3)
                for (int k = 0; k < 3; k++)
                {
                    int a = tris[t + k], b = tris[t + (k + 1) % 3];
                    adj[a].Add(b);
                    adj[b].Add(a);
                }

            var queue = new SortedSet<(float d, int i)>();
            for (int i = 0; i < verts.Length; i++)
            {
                dist[i] = covered[i] ? 0f : float.MaxValue;
                if (covered[i]) queue.Add((0f, i));
            }
            while (queue.Count > 0)
            {
                var (d, i) = queue.Min;
                queue.Remove(queue.Min);
                if (d > dist[i]) continue;
                foreach (int j in adj[i])
                {
                    float nd = d + Vector3.Distance(verts[i], verts[j]);
                    if (nd < dist[j])
                    {
                        if (dist[j] != float.MaxValue) queue.Remove((dist[j], j));
                        dist[j] = nd;
                        queue.Add((nd, j));
                    }
                }
            }

            var uv1 = new Vector2[verts.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                if (dist[i] == float.MaxValue) dist[i] = 999f;
                uv1[i] = new Vector2(dist[i], covered[i] ? 1f : 0f);
            }
            sea.uv2 = uv1;
            return dist;
        }

        // ---------------------------------------------------------------- morceaux

        private static Vector3[] FibonacciDirections(int n)
        {
            var dirs = new Vector3[n];
            float golden = Mathf.PI * (3f - Mathf.Sqrt(5f));
            for (int i = 0; i < n; i++)
            {
                float y = 1f - (i + 0.5f) / n * 2f;
                float r = Mathf.Sqrt(1f - y * y);
                float a = golden * i;
                dirs[i] = new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
            }
            return dirs;
        }

        // decoupe par direction depuis le centre (chaque triangle va au morceau dont la direction est la plus proche de son centre)
        private static List<Mesh> Chunk(Mesh src, Vector3[] dirs, string name)
        {
            var verts = src.vertices;
            var normals = src.normals;
            var tangents = src.tangents;
            var colors = src.colors;
            var uv0 = new List<Vector2>(); src.GetUVs(0, uv0);
            var uv1 = new List<Vector2>(); src.GetUVs(1, uv1);
            int subCount = src.subMeshCount;

            var buckets = new List<int>[dirs.Length, subCount];
            for (int c = 0; c < dirs.Length; c++)
                for (int s = 0; s < subCount; s++)
                    buckets[c, s] = new List<int>();
            for (int s = 0; s < subCount; s++)
            {
                var tris = src.GetTriangles(s);
                for (int t = 0; t < tris.Length; t += 3)
                {
                    var centroid = verts[tris[t]] + verts[tris[t + 1]] + verts[tris[t + 2]];
                    int best = 0;
                    float bestDot = float.MinValue;
                    for (int c = 0; c < dirs.Length; c++)
                    {
                        float d = Vector3.Dot(centroid, dirs[c]);
                        if (d > bestDot) { bestDot = d; best = c; }
                    }
                    buckets[best, s].Add(tris[t]);
                    buckets[best, s].Add(tris[t + 1]);
                    buckets[best, s].Add(tris[t + 2]);
                }
            }

            var map = new int[verts.Length];
            var chunks = new List<Mesh>();
            for (int c = 0; c < dirs.Length; c++)
            {
                if (Enumerable.Range(0, subCount).All(s => buckets[c, s].Count == 0)) continue;
                System.Array.Fill(map, -1);
                var used = new List<int>();
                var subTris = new List<int>[subCount];
                for (int s = 0; s < subCount; s++)
                {
                    subTris[s] = new List<int>(buckets[c, s].Count);
                    foreach (int i in buckets[c, s])
                    {
                        if (map[i] < 0) { map[i] = used.Count; used.Add(i); }
                        subTris[s].Add(map[i]);
                    }
                }
                var m = NewMesh($"{name}_{chunks.Count:00}");
                m.SetVertices(used.Select(i => verts[i]).ToList());
                if (normals.Length > 0) m.SetNormals(used.Select(i => normals[i]).ToList());
                if (tangents.Length > 0) m.SetTangents(used.Select(i => tangents[i]).ToList());
                if (colors.Length > 0) m.SetColors(used.Select(i => colors[i]).ToList());
                if (uv0.Count > 0) m.SetUVs(0, used.Select(i => uv0[i]).ToList());
                if (uv1.Count > 0) m.SetUVs(1, used.Select(i => uv1[i]).ToList());
                m.subMeshCount = subCount;
                for (int s = 0; s < subCount; s++)
                    m.SetTriangles(subTris[s], s, false);
                m.RecalculateBounds();
                chunks.Add(m);
            }
            return chunks;
        }

        private static Mesh Combine(Mesh[] meshes, string name)
        {
            var parts = meshes.Select(m =>
            {
                var single = NewMesh("part");
                single.CombineMeshes(Enumerable.Range(0, m.subMeshCount)
                    .Select(s => new CombineInstance { mesh = m, subMeshIndex = s, transform = Matrix4x4.identity }).ToArray(), true, false);
                return new CombineInstance { mesh = single, transform = Matrix4x4.identity };
            }).ToArray();
            var result = NewMesh(name);
            result.CombineMeshes(parts, false, false);
            result.RecalculateBounds();
            foreach (var p in parts) Object.DestroyImmediate(p.mesh);
            return result;
        }

        private static Mesh BuildSphere(int lon, int lat)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            for (int y = 0; y <= lat; y++)
            {
                float v = (float)y / lat * Mathf.PI;
                for (int x = 0; x <= lon; x++)
                {
                    float u = (float)x / lon * Mathf.PI * 2f;
                    verts.Add(new Vector3(Mathf.Sin(v) * Mathf.Cos(u), Mathf.Cos(v), Mathf.Sin(v) * Mathf.Sin(u)));
                }
            }
            for (int y = 0; y < lat; y++)
                for (int x = 0; x < lon; x++)
                {
                    int a = y * (lon + 1) + x, b = a + lon + 1;
                    tris.AddRange(new[] { a, a + 1, b, a + 1, b + 1, b });
                }
            var m = new Mesh { name = "WFC_AtmoSphere" };
            m.SetVertices(verts);
            m.SetNormals(verts);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        // ---------------------------------------------------------------- assets

        private static void SaveChunks(string path, List<Mesh> meshes)
        {
            if (AssetDatabase.LoadMainAssetAtPath(path) != null)
                AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(meshes[0], path);
            for (int i = 1; i < meshes.Count; i++)
                AssetDatabase.AddObjectToAsset(meshes[i], path);
        }

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

        private class PlanetMaterials { public Material Land, Sea, Atmo, Clouds; }

        // crees une fois avec les reglages du job, puis laisses tels quels (on peut les retoucher sans que la cuisson les ecrase)
        private static PlanetMaterials EnsureMaterials(PlanetJob job, string folder, float radius)
        {
            Material Get(string suffix, string shader, System.Action<Material> init)
            {
                string path = $"{folder}/M_WFC_{job.Name}_{suffix}.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat != null) return mat;
                mat = new Material(Shader.Find(shader)) { name = Path.GetFileNameWithoutExtension(path) };
                if (mat.HasProperty("_HazeColor")) mat.SetColor("_HazeColor", job.HazeColor);
                init(mat);
                AssetDatabase.CreateAsset(mat, path);
                return mat;
            }

            var result = new PlanetMaterials
            {
                Land = Get("Land", "WFC/Planet Land", m =>
                {
                    m.SetFloat("_PlanetRadius", radius);
                    m.SetFloat("_HeightRange", radius * 0.06f);
                    m.SetColor("_RimColor", job.RimColor);
                    m.SetColor("_LowTint", job.LowTint);
                    m.SetColor("_ShadowTint", job.ShadowTint);
                    if (job.Sea == SeaStyle.Lava)
                    {
                        m.SetFloat("_Saturation", 0.9f);
                        m.SetFloat("_Brightness", 0.6f);
                        m.SetFloat("_Contrast", 1.12f);
                        m.SetFloat("_EmissionStrength", 4.5f);
                        m.SetFloat("_RimStrength", 0.2f);
                    }
                }),
                Sea = job.Sea == SeaStyle.Water
                    ? Get("Water", "WFC/Planet Water", m => { })
                    : Get("Lava", "WFC/Planet Lava", m => { }),
                Atmo = Get("Atmosphere", "WFC/Planet Atmosphere", m =>
                {
                    m.SetColor("_AtmoColor", job.AtmoColor);
                    m.SetColor("_SunsetColor", job.SunsetColor);
                    m.SetFloat("_Limb", Mathf.Sqrt(1f - 1f / (job.AtmoScale * job.AtmoScale)));
                    if (job.Sea == SeaStyle.Lava)
                    {
                        m.SetFloat("_NightSide", 0.25f); // la lave eclaire le ciel meme de nuit
                        m.SetFloat("_Intensity", 0.6f);
                    }
                }),
            };
            if (job.Clouds)
                result.Clouds = Get("Clouds", "WFC/Planet Clouds", m => { });
            return result;
        }

        // ---------------------------------------------------------------- prefabs

        private static Material[] LandMaterials(Mesh mesh, PlanetMaterials mats) =>
            Enumerable.Repeat(mats.Land, Mathf.Max(1, mesh.subMeshCount)).ToArray();

        // coques au-dessus du plus haut point du sol (montagnes, arbres): sinon elles le coupent (mouchetures au bord du disque)
        private static void AddShells(PlanetJob job, Transform parent, Mesh sphere, PlanetMaterials mats, float radius, float surfaceTop, float seaRadius)
        {
            float atmo = Mathf.Max(radius * job.AtmoScale, surfaceTop * 1.05f);
            AddShell("Atmosphere", parent, sphere, mats.Atmo, atmo);
            // limbe = bord de la mer vu sur la coque: le halo est le plus fort contre la silhouette et s'eteint vers l'espace
            float limbRadius = seaRadius > 0f ? seaRadius : radius;
            mats.Atmo.SetFloat("_Limb", Mathf.Sqrt(Mathf.Max(0.0025f, 1f - (limbRadius / atmo) * (limbRadius / atmo))));
            mats.Atmo.SetFloat("_Intensity", job.Sea == SeaStyle.Lava ? 0.5f : 0.6f);
            mats.Atmo.SetFloat("_RimPower", 11f);
            EditorUtility.SetDirty(mats.Atmo);
            if (mats.Clouds != null)
                AddShell("Clouds", parent, sphere, mats.Clouds, Mathf.Max(radius * job.CloudScale, surfaceTop * 1.01f));
        }

        // haut du relief: 99e centile du rayon des sommets (le max serait un pic isole ou le sommet d'une tour)
        private static float SeaRadius; // rayon du niveau 0 de la planete en cours de cuisson

        private static float MaxRadius(Mesh mesh)
        {
            var r = mesh.vertices.Select(v => v.magnitude).OrderBy(x => x).ToArray();
            float top = r.Length > 0 ? r[Mathf.Clamp((int)(r.Length * 0.99f), 0, r.Length - 1)] : 0f;
            Report.Add($"  surface top (p99) {top:0.0}, max {(r.Length > 0 ? r[^1] : 0f):0.0}");
            return top;
        }

        private static void AddShell(string name, Transform parent, Mesh sphere, Material mat, float radius)
        {
            var atmo = new GameObject(name);
            atmo.transform.SetParent(parent, false);
            atmo.transform.localScale = Vector3.one * radius;
            atmo.AddComponent<MeshFilter>().sharedMesh = sphere;
            var mr = atmo.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        private static GameObject BuildVisualPrefab(PlanetJob job, Mesh lobby, Mesh sphere, PlanetMaterials mats, string path)
        {
            var go = new GameObject(Path.GetFileNameWithoutExtension(path));
            go.AddComponent<MeshFilter>().sharedMesh = lobby;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = new[] { mats.Land, mats.Sea };
            mr.shadowCastingMode = ShadowCastingMode.Off;
            AddShells(job, go.transform, sphere, mats, lobby.bounds.extents.magnitude / Mathf.Sqrt(3f), MaxRadius(lobby), SeaRadius);
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }

        private static GameObject BuildGameplayPrefab(PlanetJob job, List<Mesh> land, List<Mesh> sea, Mesh sphere, Mesh collider,
            PlanetMaterials mats, float radius, float runDuration, string path)
        {
            var go = new GameObject(Path.GetFileNameWithoutExtension(path));
            const StaticEditorFlags flags = (StaticEditorFlags)127;
            GameObjectUtility.SetStaticEditorFlags(go, flags);

            MeshRenderer first = null;
            void AddChunk(string name, Mesh mesh, Material[] materials, bool castShadows)
            {
                var chunk = new GameObject(name);
                chunk.transform.SetParent(go.transform, false);
                GameObjectUtility.SetStaticEditorFlags(chunk, flags);
                chunk.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = chunk.AddComponent<MeshRenderer>();
                mr.sharedMaterials = materials;
                mr.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                first ??= mr;
            }
            for (int i = 0; i < land.Count; i++)
                AddChunk($"Land_{i:00}", land[i], LandMaterials(land[i], mats), true);
            for (int i = 0; i < sea.Count; i++)
                AddChunk($"Sea_{i:00}", sea[i], new[] { mats.Sea }, false);
            AddShells(job, go.transform, sphere, mats, radius, land.Max(MaxRadius), SeaRadius);

            var data = go.AddComponent<PlanetDataAuthoring>();
            var so = new SerializedObject(data);
            so.FindProperty("_planetID").intValue = job.PlanetId;
            so.FindProperty("_autoCalculate").boolValue = false;
            so.FindProperty("_planetRenderer").objectReferenceValue = first;
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

        private static MeshCollider Probe(Scene scene, Mesh mesh, Vector3 center)
        {
            var probe = new GameObject("WFC Probe");
            SceneManager.MoveGameObjectToScene(probe, scene);
            probe.transform.position = center;
            var mc = probe.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;
            Physics.SyncTransforms();
            return mc;
        }

        // objets poses sur l'ancienne surface: rayon vers le centre sur le nouveau collider, meme hauteur au-dessus du sol
        private static void Resnap(PlanetJob job, Scene scene, GameObject planet, Mesh collider, Vector3 center, float radius)
        {
            var meshCollider = Probe(scene, collider, center);
            var targets = new List<Transform>();
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root == planet || root == meshCollider.gameObject || !root.activeSelf) continue;
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
            Object.DestroyImmediate(meshCollider.gameObject);
            Report.Add($"[{job.Name}] re-snapped {moved} objects onto the WFC surface" +
                       (skipped.Count > 0 ? $"; left as is (not on the surface or no hit): {string.Join(", ", skipped)}" : ""));
        }

        // le joueur ne doit pas apparaitre dans l'eau ou la lave: point de terre le plus proche (sommet de mer recouvert le plus
        // proche en direction), pose sur le sol
        private static void MovePlayerStartOnLand(PlanetJob job, Scene scene, Vector3[] seaVerts, Mesh collider, float[] shore, Vector3 center, float radius)
        {
            var start = scene.GetRootGameObjects().FirstOrDefault(g => g.name.StartsWith("PF_PlayerStart"));
            if (start == null) return;
            if (seaVerts.Length == 0) return;
            Vector3 dir = (start.transform.position - center).normalized;
            int nearest = Enumerable.Range(0, seaVerts.Length).OrderByDescending(i => Vector3.Dot(seaVerts[i].normalized, dir)).First();
            if (shore[nearest] <= 0f) return; // deja sur la terre

            // terre bien a l'interieur: sommet recouvert dont les voisins le sont aussi (approxime par la direction)
            int land = Enumerable.Range(0, seaVerts.Length).Where(i => shore[i] <= 0f)
                .OrderByDescending(i => Vector3.Dot(seaVerts[i].normalized, dir)).FirstOrDefault();
            var mc = Probe(scene, collider, center);
            var landDir = seaVerts[land].normalized;
            // un peu plus loin dans les terres: moyenne avec les points de terre proches
            var near = Enumerable.Range(0, seaVerts.Length).Where(i => shore[i] <= 0f && Vector3.Dot(seaVerts[i].normalized, landDir) > Mathf.Cos(6f * Mathf.Deg2Rad));
            landDir = near.Aggregate(Vector3.zero, (acc, i) => acc + seaVerts[i].normalized).normalized;
            float height = Mathf.Max(0f, (start.transform.position - center).magnitude - radius);
            if (mc.Raycast(new Ray(center + landDir * radius * 2f, -landDir), out var hit, radius * 2f))
            {
                start.transform.position = hit.point + landDir * Mathf.Min(height, 2f);
                start.transform.rotation = Quaternion.FromToRotation(dir, landDir) * start.transform.rotation;
                Report.Add($"[{job.Name}] PF_PlayerStart was in the {(job.Sea == SeaStyle.Lava ? "lava" : "sea")}: moved {Vector3.Angle(dir, landDir) * Mathf.Deg2Rad * radius:0} m to land");
            }
            Object.DestroyImmediate(mc.gameObject);
        }

        // zones de brulure sur la lave: spheres posees au milieu des coulees, rayon borne par la distance au rivage (pas sur la roche)
        private static List<(Vector3 p, float r)> PlaceLavaHazards(PlanetJob job, Scene scene, Vector3[] verts, float[] shore, Vector3 center)
        {
            var existing = scene.GetRootGameObjects().FirstOrDefault(g => g.name == HazardRootName);
            if (existing != null) Object.DestroyImmediate(existing);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LavaHazardPrefab);
            if (prefab == null) { Report.Add($"[{job.Name}] {LavaHazardPrefab} not found, no lava hazards"); return new List<(Vector3 p, float r)>(); }

            const float minRadius = 5f, maxRadius = 18f;
            var candidates = Enumerable.Range(0, verts.Length).Where(i => shore[i] >= minRadius - 1f && shore[i] < 900f)
                .OrderByDescending(i => shore[i]).ToList();
            var chosen = new List<(Vector3 p, float r)>();
            foreach (int i in candidates)
            {
                float r = Mathf.Clamp(shore[i] + 1f, minRadius, maxRadius);
                var p = verts[i];
                if (chosen.Any(c => (c.p - p).magnitude < (c.r + r) * 0.85f)) continue;
                chosen.Add((p, r));
            }

            var root = new GameObject(HazardRootName);
            SceneManager.MoveGameObjectToScene(root, scene);
            for (int k = 0; k < chosen.Count; k++)
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                inst.name = $"PF_HazardZone_Lava (WFC {k:000})";
                inst.transform.SetParent(root.transform, false);
                inst.transform.position = center + chosen[k].p;
                var auth = inst.GetComponent<HazardZoneAuthoring>();
                if (auth != null)
                {
                    var so = new SerializedObject(auth);
                    so.FindProperty("Radius").floatValue = chosen[k].r;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
            }
            Report.Add($"[{job.Name}] placed {chosen.Count} lava hazard zones (radius {minRadius}-{maxRadius}) under '{HazardRootName}'");
            return chosen;
        }

        // ---------------------------------------------------------------- ambiance

        // etalonnage propre a la planete (Volume global dans la sous-scene: actif seulement quand la planete est chargee)
        // + braises qui montent des plus grandes coulees de lave. Volume et ParticleSystem sont des companions Entities
        private static void BuildAmbience(PlanetJob job, Scene scene, string folder, Vector3 center, List<(Vector3 p, float r)> lava)
        {
            var existing = scene.GetRootGameObjects().FirstOrDefault(g => g.name == AmbienceRootName);
            if (existing != null) Object.DestroyImmediate(existing);
            var root = new GameObject(AmbienceRootName);
            SceneManager.MoveGameObjectToScene(root, scene);
            root.transform.position = center;

            var grade = new GameObject("Planet Grade");
            grade.transform.SetParent(root.transform, false);
            var volume = grade.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 20f;
            volume.sharedProfile = EnsureProfile(job, folder);

            int embers = 0;
            if (job.Sea == SeaStyle.Lava && lava.Count > 0)
            {
                var mat = EnsureEmberMaterial(folder);
                foreach (var (p, r) in lava.OrderByDescending(h => h.r).Take(14))
                {
                    CreateEmbers(root.transform, center + p, p.normalized, r, mat, embers);
                    embers++;
                }
            }
            Report.Add($"[{job.Name}] ambience: planet grade volume{(embers > 0 ? $", {embers} ember emitters over the lava" : "")}");
        }

        private static VolumeProfile EnsureProfile(PlanetJob job, string folder)
        {
            string path = $"{folder}/VP_WFC_{job.Name}.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile != null) return profile;
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);
            var bloom = profile.Add<Bloom>();
            var color = profile.Add<ColorAdjustments>();
            var vignette = profile.Add<Vignette>();
            if (job.Sea == SeaStyle.Lava)
            {
                bloom.intensity.Override(0.85f);
                bloom.threshold.Override(0.95f);
                bloom.scatter.Override(0.7f);
                bloom.tint.Override(new Color(1f, 0.85f, 0.7f));
                color.contrast.Override(14f);
                color.saturation.Override(8f);
                color.colorFilter.Override(new Color(1f, 0.94f, 0.88f));
                vignette.intensity.Override(0.32f);
                vignette.color.Override(new Color(0.18f, 0.03f, 0f));
                vignette.smoothness.Override(0.45f);
            }
            else
            {
                bloom.intensity.Override(0.45f);
                bloom.threshold.Override(1f);
                color.contrast.Override(10f);
                color.saturation.Override(14f);
                vignette.intensity.Override(0.24f);
                vignette.color.Override(new Color(0.02f, 0.05f, 0.14f));
                vignette.smoothness.Override(0.45f);
            }
            foreach (var component in profile.components)
            {
                component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(component, profile);
            }
            EditorUtility.SetDirty(profile);
            return profile;
        }

        private static Material EnsureEmberMaterial(string folder)
        {
            string path = $"{folder}/M_WFC_Ember.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            mat = new Material(Shader.Find("WFC/Ember")) { name = "M_WFC_Ember" };
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        private static void CreateEmbers(Transform parent, Vector3 position, Vector3 up, float radius, Material mat, int index)
        {
            var go = new GameObject($"Embers_{index:00}");
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.transform.rotation = Quaternion.FromToRotation(Vector3.up, up);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = true;
            main.duration = 5f;
            main.prewarm = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 5.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.95f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.62f, 0.15f), new Color(1f, 0.28f, 0.04f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 200;
            main.gravityModifier = 0f;

            var emission = ps.emission;
            emission.rateOverTime = Mathf.Clamp(radius * 1.6f, 8f, 30f);

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = radius * 0.85f;
            shape.rotation = new Vector3(90f, 0f, 0f); // cercle a plat sur la lave (plan XZ local)

            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
            velocity.y = new ParticleSystem.MinMaxCurve(1.2f, 3.8f);
            velocity.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.9f;
            noise.frequency = 0.35f;
            noise.scrollSpeed = 0.4f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(new Color(1f, 0.85f, 0.4f), 0f), new GradientColorKey(new Color(1f, 0.35f, 0.05f), 0.5f), new GradientColorKey(new Color(0.6f, 0.08f, 0.02f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = gradient;

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.25f));

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = mat;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            ps.Play();
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
                foreach (var t in inst.GetComponentsInChildren<Transform>(true))
                    t.gameObject.layer = target.gameObject.layer;
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
