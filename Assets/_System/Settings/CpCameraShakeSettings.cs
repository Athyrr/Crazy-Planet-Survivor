using UnityEngine;

namespace _System.Settings
{
    /// <summary>
    /// Central tuning for the player camera shake, one profile per incoming damage source category
    /// (<see cref="EDamageShakeSource"/>). Resolved as a singleton via <see cref="CpSettings{T}.I"/>
    /// (auto-loaded from Resources/Settings, auto-created in the editor) and consumed by
    /// <see cref="ShakeFeedbackComponent"/>, which picks the profile of the strongest source hitting
    /// the player this frame and drives the Cinemachine Perlin noise with it.
    /// </summary>
    [CreateAssetMenu(fileName = "CameraShakeSettings", menuName = "CPSettings/CameraShakeSettings")]
    public class CpCameraShakeSettings : CpSettings<CpCameraShakeSettings>
    {
        [System.Serializable]
        public struct ShakeProfile
        {
            [Tooltip("Cinemachine Perlin amplitude gain. 0 = no shake.")]
            public float Amplitude;

            [Tooltip("Cinemachine Perlin frequency gain.")]
            public float Frequency;

            [Tooltip("How long (seconds) the shake stays applied before fading out.")]
            public float Duration;
        }

        [Header("Direct hits")]
        [Tooltip("Final boss: contact, projectile or area attack. Strongest.")]
        [SerializeField]
        private ShakeProfile _boss = new ShakeProfile { Amplitude = 6f, Frequency = 1.2f, Duration = 0.25f };

        [Tooltip("Elite (mini-boss): contact, projectile or area attack.")]
        [SerializeField]
        private ShakeProfile _elite = new ShakeProfile { Amplitude = 4f, Frequency = 1f, Duration = 0.18f };

        [Tooltip("Explosion AoE (Explosive tag).")]
        [SerializeField]
        private ShakeProfile _explosion = new ShakeProfile { Amplitude = 5f, Frequency = 1.5f, Duration = 0.2f };

        [Tooltip("Normal enemy: contact, projectile or area attack.")]
        [SerializeField]
        private ShakeProfile _enemy = new ShakeProfile { Amplitude = 2.5f, Frequency = 1f, Duration = 0.12f };

        [Header("Damage over time")]
        [Tooltip("Burn ticks, tick zones, environmental hazard (lava). Lightest — keep subtle to avoid spam.")]
        [SerializeField]
        private ShakeProfile _dot = new ShakeProfile { Amplitude = 0.8f, Frequency = 0.8f, Duration = 0.08f };

        [Header("Fallback")]
        [Tooltip("Used for EDamageShakeSource.None (unstamped damage, player death shake).")]
        [SerializeField]
        private ShakeProfile _default = new ShakeProfile { Amplitude = 5f, Frequency = 1f, Duration = 0.1f };

        /// <summary>Resolves the shake profile for a given damage-source category.</summary>
        public ShakeProfile GetProfile(EDamageShakeSource source)
        {
            switch (source)
            {
                case EDamageShakeSource.Boss: return _boss;
                case EDamageShakeSource.Elite: return _elite;
                case EDamageShakeSource.Explosion: return _explosion;
                case EDamageShakeSource.Enemy: return _enemy;
                case EDamageShakeSource.DoT: return _dot;
                default: return _default;
            }
        }
    }
}
