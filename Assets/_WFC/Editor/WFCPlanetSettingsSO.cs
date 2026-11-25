using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WFCContent.WFC;

namespace Editor
{
    /// <summary>
    /// WFC build settings of one planet (editor only), read by WFCCrazyPlanetBaker in place of its former hard-coded
    /// job table. The planet is identified by its PlanetSO: the tool opens Planet.Scene, writes Planet.VisualPrefab and
    /// finds the lobby visual to replace through the PlanetComponent that selects Planet.
    /// </summary>
    [CreateAssetMenu(fileName = "WFCPlanetSettings_New", menuName = "WFC/Crazy Planet Settings", order = 3)]
    public class WFCPlanetSettingsSO : ScriptableObject
    {
        [Tooltip("Planet this asset builds: its scene is rewritten, its VisualPrefab is written.")]
        public PlanetSO Planet;

        [Tooltip("Name used in every generated path: Assets/_Prefabs/Planets/WFC_<OutputName>/PF_WFC_Planet_<OutputName>*.")]
        public string OutputName;

        public WFCPlanetTheme Theme;
        public int Density = 4;
        public int Seed = 67;

        [Tooltip("Radius used when the scene has no planet yet.")]
        public float FallbackRadius = 100f;

        [Tooltip("Scene roots placed for the previous relief: disabled (not destroyed) by the first WFC bake.")]
        public string[] DisableRoots = new string[0];

        public WFCCrazyPlanetBaker.SeaStyle Sea;
        public bool LavaHazards;
        public Color RimColor, AtmoColor, SunsetColor, LowTint, ShadowTint, HazeColor;
        public float AtmoScale = 1.12f;
        public bool Clouds;
        public float CloudScale = 1.055f;

        /// <summary>Asset path of the theme (the tool loads it by path).</summary>
        public string ThemePath => Theme != null ? AssetDatabase.GetAssetPath(Theme) : null;

        /// <summary>Asset path of Planet.Scene, or null.</summary>
        public string ScenePath => ScenePathOf(Planet);

        /// <summary>
        /// Asset path of a planet's scene. EntitySceneReference exposes no GUID: read the serialized uint4
        /// (Scene.Id.GlobalId.AssetGUID.Value) and rebuild the GUID string, each uint holding 8 hex chars reversed.
        /// </summary>
        public static string ScenePathOf(PlanetSO planet)
        {
            if (planet == null)
                return null;
            using var so = new SerializedObject(planet);
            SerializedProperty value = so.FindProperty("Scene.Id.GlobalId.AssetGUID.Value");
            if (value == null)
                return null;
            var guid = new System.Text.StringBuilder(32);
            foreach (string axis in new[] { "x", "y", "z", "w" })
            {
                string hex = ((uint)value.FindPropertyRelative(axis).longValue).ToString("x8");
                for (int i = 7; i >= 0; i--)
                    guid.Append(hex[i]);
            }
            string path = AssetDatabase.GUIDToAssetPath(guid.ToString());
            return string.IsNullOrEmpty(path) ? null : path;
        }

        [ContextMenu("Bake this planet (WFC)")]
        private void BakeThisPlanet()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            WFCCrazyPlanetBaker.BakeOne(this);
        }
    }
}
