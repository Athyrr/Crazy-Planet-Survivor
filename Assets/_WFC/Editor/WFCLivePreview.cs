using UnityEditor;
using UnityEngine;
using WFCContent.WFC;
using WFCContent.World;

namespace Editor
{
    // regenere les planetes de la scene quand une regle WFC change (debounce pour ne pas rebuild a chaque clic)
    [InitializeOnLoad]
    public static class WFCLivePreview
    {
        private const string EnabledPrefKey = "WFC.LivePreview.Enabled";
        private const double DebounceSeconds = 0.25;

        private static double pendingAt = -1;

        public static event System.Action Regenerated;

        public static bool Enabled
        {
            get => EditorPrefs.GetBool(EnabledPrefKey, true);
            set => EditorPrefs.SetBool(EnabledPrefKey, value);
        }

        static WFCLivePreview()
        {
            WFCElementData.RulesChanged += OnRulesChanged;
            EditorApplication.update += Tick;
        }

        private static void OnRulesChanged()
        {
            if (Enabled)
                pendingAt = EditorApplication.timeSinceStartup + DebounceSeconds;
        }

        private static void Tick()
        {
            if (pendingAt < 0 || EditorApplication.timeSinceStartup < pendingAt)
                return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;

            pendingAt = -1;
            RegenerateAll();
        }

        public static void RegenerateAll()
        {
            var planets = Object.FindObjectsByType<GoldbergPolyhedron>();
            foreach (var planet in planets)
            {
                if (!planet.isActiveAndEnabled)
                    continue;
                try
                {
                    planet.Generate();
                }
                catch (System.Exception e)
                {
                    Debug.LogException(e, planet);
                }
            }

            SceneView.RepaintAll();
            Regenerated?.Invoke();
        }
    }
}
