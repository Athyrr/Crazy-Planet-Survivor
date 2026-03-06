using System;
using System.Linq;
using UnityEngine;
using UnityEngine.VFX;

namespace _Material.Planets.Renderer.Wind
{
    public class FxSelectorRenderer : MonoBehaviour
    {
        [Serializable]
        public enum EFxSelectorRenderer
        {
            NONE,
            WIND,
            LAVA
        }

        #region Members
        
        [Header("Reference")] 
        [SerializeField] private EnumValues<EFxSelectorRenderer, VisualEffect> _vfxEnabled;
        [SerializeField] private EnumValues<EPlanetID, EFxSelectorRenderer> _enabled;

        #endregion

        #region Core

        private void OnEnable()
        {
            if (GameManager.Instance != null)
                GameManager.Instance.OnPlanetSelected += OnGameStateChanged;
        }

        private void OnDisable()
        {
            if (GameManager.Instance != null)
                GameManager.Instance.OnPlanetSelected -= OnGameStateChanged;
        }

        private void OnGameStateChanged(EPlanetID planetID)
        {
            var vfx = _enabled[planetID];
            
            // step 1 disable all fx
            var vfxEnabled = _vfxEnabled.ToList();
            foreach (var value in vfxEnabled)
                if (value.Value!= null) value.Value.gameObject.SetActive(false);
            
            // step 2 enable selected
            if (_vfxEnabled[vfx] != null)
                _vfxEnabled[vfx].gameObject.SetActive(true);
        }

        #endregion
    }
}