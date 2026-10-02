using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Editor
{
    // captures des planetes de Crazy Planet (eclairage de SC_Main + scene de la planete), pour comparer avant/apres sans ouvrir le jeu
    // batch: Unity -batchmode -projectPath ... -executeMethod Editor.WFCPlanetScreens.CaptureBatch -screensTag before
    public static class WFCPlanetScreens
    {
        private const string MainScene = "Assets/_Scenes/SC_Main.unity";
        private static readonly (string name, string scene)[] Planets =
        {
            ("Earth", "Assets/_Scenes/SC_Main/SC_Earth.unity"),
            ("Volcanus", "Assets/_Scenes/SC_Main/SC_Volcanus.unity"),
        };

        public static string OutputFolder = "D:/Projet/Unity/modding/WFC/Exports/screens";

        public static void CaptureBatch()
        {
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-screensTag");
            string tag = i >= 0 && i + 1 < args.Length ? args[i + 1] : "shot";
            try
            {
                CaptureAll(tag);
                EditorApplication.Exit(0);
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }

        [MenuItem("Tools/WFC/Capture Crazy Planet Screens")]
        public static void CaptureMenu() => CaptureAll("manual");

        public static void CaptureAll(string tag)
        {
            Directory.CreateDirectory(OutputFolder);
            foreach (var (name, scenePath) in Planets)
            {
                var main = EditorSceneManager.OpenScene(MainScene, OpenSceneMode.Single);
                var planetScene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                SceneManager.SetActiveScene(main);

                var data = Object.FindObjectsByType<PlanetDataAuthoring>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).FirstOrDefault();
                Vector3 center = data != null ? (Vector3)data.WorldCenter : Vector3.zero;
                float radius = data != null ? data.WorldRadius : 100f;
                var sun = RenderSettings.sun != null ? RenderSettings.sun.transform.forward : new Vector3(-0.3f, -0.8f, 0.5f).normalized;
                // les particules ne tournent pas en batch: on les avance de quelques secondes avant la capture
                foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                    ps.Simulate(4f, true, true);
                var start = planetScene.GetRootGameObjects().FirstOrDefault(g => g.name.StartsWith("PF_PlayerStart"));
                Vector3 player = start != null ? start.transform.position : center + Vector3.up * radius;


                var camGo = new GameObject("Screens Camera");
                var cam = camGo.AddComponent<Camera>();
                cam.fieldOfView = 45f;
                cam.nearClipPlane = 0.3f;
                cam.farClipPlane = radius * 20f;
                cam.clearFlags = CameraClearFlags.Skybox;
                var urp = camGo.AddComponent<UniversalAdditionalCameraData>();
                urp.renderPostProcessing = true;
                urp.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

                // orbite: face eclairee, un peu de terminateur sur le cote
                Vector3 litDir = Quaternion.AngleAxis(35f, Vector3.up) * -sun;
                Place(cam, center + (litDir.normalized + Vector3.up * 0.25f).normalized * radius * 3.1f, center);
                Save(cam, $"{tag}_{name}_orbit");

                // jeu: au-dessus du joueur, vue plongeante (camera de survivor)
                Vector3 up = (player - center).normalized;
                Vector3 fwd = Vector3.ProjectOnPlane(Vector3.forward, up).normalized;
                if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.ProjectOnPlane(Vector3.right, up).normalized;
                cam.fieldOfView = 40f;
                Place(cam, player + up * 38f - fwd * 26f, player, up);
                Save(cam, $"{tag}_{name}_game");

                // horizon: camera basse pour voir le relief et l'atmosphere
                Place(cam, player + up * 9f - fwd * 30f, player + fwd * 40f + up * 2f, up);
                cam.fieldOfView = 55f;
                Save(cam, $"{tag}_{name}_horizon");

                // vue "Scene view": camera de type SceneView, loin (zoom arriere), sans post

                urp.renderPostProcessing = false;
                cam.fieldOfView = 60f;
                Place(cam, center + (litDir.normalized + Vector3.up * 0.25f).normalized * radius * 9f, center);
                Save(cam, $"{tag}_{name}_sceneview_far");
                Place(cam, center + (litDir.normalized + Vector3.up * 0.25f).normalized * radius * 2.2f, center);
                Save(cam, $"{tag}_{name}_sceneview_near");
                cam.orthographic = true;
                cam.orthographicSize = radius * 2.5f;
                Save(cam, $"{tag}_{name}_sceneview_ortho");
                Object.DestroyImmediate(camGo);
            }
        }

        private static void Place(Camera cam, Vector3 position, Vector3 target, Vector3? up = null)
        {
            cam.transform.position = position;
            cam.transform.rotation = Quaternion.LookRotation(target - position, up ?? Vector3.up);
        }

        private static void Save(Camera cam, string file)
        {
            const int w = 1600, h = 900;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            // deux rendus: le premier initialise l'historique (exposition, temporels)
            cam.Render();
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            cam.targetTexture = null;
            File.WriteAllBytes(Path.Combine(OutputFolder, file + ".png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            rt.Release();
            Object.DestroyImmediate(rt);
            Debug.Log($"[WFCScreens] {file}.png");
        }
    }
}
