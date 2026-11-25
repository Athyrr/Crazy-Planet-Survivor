using System;
using UnityEngine;

namespace _System.Settings
{
    [CreateAssetMenu(fileName = "UISettings", menuName = "CPSettings/UISettings")]
    public class CpBaseCameraSettings: CpSettings<CpBaseCameraSettings>
    {
        [Serializable]
        public struct CameraSettingsData
        {
            public float VerticalAxis;
            public float RadialAxis;

            [Tooltip("Base distance the camera sits above the player's surface (added to the player's " +
                     "distance from the planet centre). Replaces the old hard-coded +35.")]
            public float RadiusOffset;

            public float LookAtOffsetY;
        }
        
        [Header("Global camera")]
        [Tooltip("Camera of every planet that does not override it (PlanetSO.OverrideCamera). Read live: tunable in Play.")]
        [SerializeField] private CameraSettingsData _default;

        #region Accessor

        /// <summary>Global camera, read live from the settings asset.</summary>
        public static CameraSettingsData Default => I._default;

        /// <summary>The single camera resolution rule: the planet's override when it has one, else the global camera.</summary>
        public static CameraSettingsData Resolve(bool overrideCamera, CameraSettingsData planetCamera) =>
            overrideCamera ? planetCamera : Default;

        #endregion
    }
}