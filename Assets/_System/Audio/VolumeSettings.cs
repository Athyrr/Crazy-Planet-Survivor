using System;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;

namespace _System.Audio
{
    public class VolumeSettings : MonoBehaviour
    {
        [Header("Master bus")]
        [SerializeField]
        private string masterBusPath = "bus:/";

        [Header("Global parameters")]
        [SerializeField]
        private string musicParameter = "MenuVol";

        [SerializeField]
        private string sfxParameter = "MenuSFX";

        [SerializeField]
        private float musicParameterMax = 1f;

        [SerializeField]
        private float sfxParameterMax = 1f;

        [Header("Master bus slider response")]
        [SerializeField]
        private bool useDecibelCurve = true;

        [SerializeField]
        private float minDecibels = -40f;

        private Bus _masterBus;
        private bool _masterBusValid;

        private void OnEnable()
        {
            ResolveMasterBus();
            Apply();
            GameSettings.Changed += Apply;
        }

        private void OnDisable()
        {
            GameSettings.Changed -= Apply;
        }

        private void Apply()
        {
            if (_masterBusValid)
                _masterBus.setVolume(SliderToGain(GameSettings.MasterVolume));

            SetParameter(musicParameter, GameSettings.MusicVolume * musicParameterMax);
            SetParameter(sfxParameter, GameSettings.SfxVolume * sfxParameterMax);
        }

        private void ResolveMasterBus()
        {
            _masterBusValid = false;

            if (string.IsNullOrWhiteSpace(masterBusPath))
                return;

            try
            {
                _masterBus = RuntimeManager.GetBus(masterBusPath);
                _masterBusValid = true;
            }
            catch (Exception e)
            {
                Debug.LogWarning(
                    $"[VolumeSettings] Master bus '{masterBusPath}' not found: {e.Message}"
                );
            }
        }

        private static void SetParameter(string name, float value)
        {
            if (!string.IsNullOrWhiteSpace(name))
                RuntimeManager.StudioSystem.setParameterByName(name, 1 - value);
        }

        private float SliderToGain(float volume01)
        {
            volume01 = Mathf.Clamp01(volume01);

            if (!useDecibelCurve)
                return volume01;

            if (volume01 <= 0f)
                return 0f;

            var db = Mathf.Lerp(minDecibels, 0f, volume01);
            return Mathf.Pow(10f, db / 20f);
        }
    }
}
