using UnityEngine;

namespace _System.Settings
{
    /// <summary>
    /// Single source of truth for combat-effect tuning: life steal, burn, stun, slow, knockback,
    /// plus the status-effect VFX prefabs. Edited as an asset (Resources/Settings) and resolved as a
    /// singleton via <see cref="CpSettings{T}.I"/>.
    ///
    /// The ECS side does not read this SO directly (Burst can't touch managed objects): the authorings
    /// (<see cref="ActiveEffectsConfigAuthoring"/>, <see cref="LifeStealConfigAuthoring"/>) bake its
    /// values into blittable singleton components (ActiveEffectsConfig, LifeStealConfig) — rebake the
    /// SC_Entity_Core subscene after changing values here.
    /// </summary>
    [CreateAssetMenu(
        fileName = "CombatEffectsSettings",
        menuName = "CPSettings/CombatEffectsSettings")]
    public class CpCombatEffectsSettings : CpSettings<CpCombatEffectsSettings>
    {
        [Header("Life Steal")]
        [Tooltip("Fraction of a hit's damage healed on a successful life-steal proc. 0.075 = 7.5%.")]
        [SerializeField] private float _lifeStealConversion = 0.075f;

        [Tooltip("Minimum seconds between two life-steal procs (rate limiter). 0.1 = max 10 procs/s.")]
        [SerializeField] private float _lifeStealProcCooldown = 0.1f;

        public float LifeStealConversion => _lifeStealConversion;
        public float LifeStealProcCooldown => _lifeStealProcCooldown;

        [Header("Burn")]
        [Tooltip("Multiplier applied to the spell's damage to compute burn tick damage. 0.125 = 12.5%.")]
        [SerializeField] private float _burnDamageRatio = 0.125f;
        [SerializeField] private float _burnDuration = 3f;
        [Tooltip("Seconds between two burn ticks. 0.3 = a tick every 0.3s.")]
        [SerializeField] private float _burnTickRate = 0.3f;

        public float BurnDamageRatio => _burnDamageRatio;
        public float BurnDuration => _burnDuration;
        public float BurnTickRate => _burnTickRate;

        [Header("Stun")]
        [SerializeField] private float _stunDuration = 1.5f;

        public float StunDuration => _stunDuration;

        [Header("Slow")]
        [Tooltip("Multiplier applied to the target's speed.")]
        [SerializeField] private float _slowMultiplier = 2f;
        [SerializeField] private float _slowDuration = 3f;

        public float SlowMultiplier => _slowMultiplier;
        public float SlowDuration => _slowDuration;

        [Header("Knockback")]
        [SerializeField] private float _knockbackForce = 50f;
        [SerializeField] private float _knockbackDuration = 0.5f;

        public float KnockbackForce => _knockbackForce;
        public float KnockbackDuration => _knockbackDuration;

        [Header("Status Effect VFX")]
        [SerializeField] private GameObject _burnEffectPrefab;
        [SerializeField] private GameObject _stunEffectPrefab;
        [SerializeField] private GameObject _slowEffectPrefab;

        public GameObject BurnEffectPrefab => _burnEffectPrefab;
        public GameObject StunEffectPrefab => _stunEffectPrefab;
        public GameObject SlowEffectPrefab => _slowEffectPrefab;
    }
}
