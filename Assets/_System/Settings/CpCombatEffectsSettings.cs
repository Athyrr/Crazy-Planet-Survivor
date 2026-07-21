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
        // Punchy default: strong initial push over a short-ish window that still leaves room for the
        // "slide + settle" shape encoded in the force curve below.
        [SerializeField] private float _knockbackForce = 90f;
        [SerializeField] private float _knockbackDuration = 0.7f;

        [Tooltip("Force falloff over the knockback duration. X = normalized elapsed time (0 = impact, " +
                 "1 = end). Y = fraction of KnockbackForce applied at that time. Default = impact spike " +
                 "then quick decay and a short slide, so the hit reads as punchy and the enemy visibly slides.")]
        [SerializeField] private AnimationCurve _knockbackForceCurve = new AnimationCurve(
            new Keyframe(0.00f, 1.00f, 0f, 0f),   // full force at impact
            new Keyframe(0.10f, 0.85f, 0f, 0f),   // brief peak sustain — reads as the actual hit
            new Keyframe(0.35f, 0.30f, 0f, 0f),   // fast decay — most speed bled off here
            new Keyframe(1.00f, 0.00f, 0f, 0f));  // trailing slide down to rest

        [Tooltip("How many samples the curve is baked into for Burst evaluation.")]
        [SerializeField, Range(8, 128)] private int _knockbackCurveResolution = 32;

        public float KnockbackForce => _knockbackForce;
        public float KnockbackDuration => _knockbackDuration;
        public AnimationCurve KnockbackForceCurve => _knockbackForceCurve;
        public int KnockbackCurveResolution => _knockbackCurveResolution;

        [Header("Status Effect VFX")]
        [SerializeField] private GameObject _burnEffectPrefab;
        [SerializeField] private GameObject _stunEffectPrefab;
        [SerializeField] private GameObject _slowEffectPrefab;

        public GameObject BurnEffectPrefab => _burnEffectPrefab;
        public GameObject StunEffectPrefab => _stunEffectPrefab;
        public GameObject SlowEffectPrefab => _slowEffectPrefab;
    }
}
