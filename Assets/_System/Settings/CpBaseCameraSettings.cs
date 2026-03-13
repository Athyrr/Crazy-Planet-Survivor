using System;
using UnityEngine;

namespace _System.Settings
{
    [CreateAssetMenu(fileName = "UISettings", menuName = "CPSettings/UISettings")]
    public class CpBaseCameraSettings: CPCustomSettings<CpBaseCameraSettings>
    {
        [Serializable]
        public struct CameraSettingsData
        {
            public float VerticalAxis;
            public float RadialAxis;
            
            public float LookAtOffsetY;
        }
        
        [Header("Projet Setting")] 
        [SerializeField] private EnumValues<EPlanetID, CameraSettingsData> _planetCameraSettings;
        
        #region Accessor

        public static EnumValues<EPlanetID, CameraSettingsData> PlanetCameraSettings => I._planetCameraSettings;

        #endregion
    }
}