using System;
using System.Collections.Generic;
using _System.ECS.Authorings.Spawner;
using UnityEngine;

namespace _System.Settings
{
    [CreateAssetMenu(fileName = "UISettings", menuName = "CPSettings/UISettings")]
    public class CpBasePlanetSettings: CPCustomSettings<CpBasePlanetSettings>
    {
        [Serializable]
        public struct PlanetConfigData
        {

            public EPlanetDifficulty Difficulty;
            public SOWaveData.WaveData[] Waves;
        }
        
        [Header("Projet Setting")] 
        [SerializeField] private EnumValues<EPlanetID, List<PlanetConfigData>> _planetColors;
        
        #region Accessor

        public static EnumValues<EPlanetID, List<PlanetConfigData>> PlanetColors => I._planetColors;

        #endregion
    }
}