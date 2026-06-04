using System;
using UnityEngine;

namespace _System.Settings
{
    [CreateAssetMenu(fileName = "Player", menuName = "CPSettings/Player")]
    public class CpBasePlayerSettings: CPCustomSettings<CpBasePlayerSettings>
    {
        [Header("Movement Setting")] 
        [SerializeField, Range(0f, 1000f)] private float _playerMovementMitigationSpeed;
        
        #region Accessor

        public static float PlayerMovementMitigationSpeed => I._playerMovementMitigationSpeed;

        #endregion
    }
}