using System;
using System.Linq;
using UnityEngine;
using UnityEngine.VFX;

namespace _Material.Planets.Renderer.Wind
{
    public class FxSelectorRenderer : MonoBehaviour
    {
        #region Members

        [Header("Reference")]
        [SerializeField] private EnumValues<EPlanetFx, VisualEffect> _vfxEnabled;
        [SerializeField] private EnumValues<EPlanetID, EPlanetFx> _enabled;

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

        private void OnGameStateChanged(PlanetSO planet)
        {
            EPlanetFx fx = planet != null ? planet.Fx : EPlanetFx.None;
            
            // step 1 disable all fx
            var vfxEnabled = _vfxEnabled.ToList();
            foreach (var value in vfxEnabled)
                if (value.Value!= null) value.Value.gameObject.SetActive(false);
            
            // step 2 enable selected
            if (_vfxEnabled[fx] != null)
                _vfxEnabled[fx].gameObject.SetActive(true);
        }

        #endregion
    }
}