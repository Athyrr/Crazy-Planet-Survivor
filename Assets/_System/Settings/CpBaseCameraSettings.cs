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
        
        [Header("Projet Setting")] 
        [SerializeField] private EnumValues<EPlanetID, CameraSettingsData> _planetCameraSettings;
        
        #region Accessor

        public static EnumValues<EPlanetID, CameraSettingsData> PlanetCameraSettings => I._planetCameraSettings;

        #endregion
    }
}