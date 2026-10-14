using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using WFCContent.World;

namespace Editor
{
    // reimport des stamps + regeneration des planetes + recalcul de l'eclairage ambiant (sonde ambiante et reflet du ciel)
    public static class WFCAmbientLighting
    {
        [MenuItem("Tools/WFC/Rebuild Planet + Ambient Lighting")]
        public static void RebuildAndLight()
        {
            WFCStampImporter.ReimportAll();
            WFCStampImporter.ValidateAll();

            foreach (var planet in Object.FindObjectsByType<GoldbergPolyhedron>())
            {
                planet.Generate();
                Debug.Log($"[WFC] {planet.name}: {planet.LastSlotCount} slots, {planet.LastConflictCount} conflit(s)");
            }

            GenerateAmbient();
        }

        [MenuItem("Tools/WFC/Generate Ambient Lighting")]
        public static void GenerateAmbient()
        {
            var scene = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(scene.path))
            {
                Debug.LogError("[WFC] Sauvegarde la scene avant de generer l'eclairage.");
                return;
            }

            // les stamps sont generes (pas statiques): pas de lightmaps, seulement l'ambiant et le reflet calcules depuis le ciel
            string settingsPath = scene.path.Replace(".unity", "_Lighting.lighting");
            var settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(settingsPath);
            if (settings == null)
            {
                settings = new LightingSettings();
                AssetDatabase.CreateAsset(settings, settingsPath);
            }
            settings.bakedGI = false;
            settings.realtimeGI = false;
            Lightmapping.lightingSettings = settings;

            Lightmapping.bakeCompleted -= OnBakeCompleted;
            Lightmapping.bakeCompleted += OnBakeCompleted;
            if (!Lightmapping.BakeAsync())
                Debug.LogError("[WFC] Generate Lighting n'a pas pu demarrer.");
        }

        private static void OnBakeCompleted()
        {
            Lightmapping.bakeCompleted -= OnBakeCompleted;
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log($"[WFC] Eclairage ambiant recalcule (mode {RenderSettings.ambientMode}), pense a sauvegarder la scene.");
        }
    }
}
