using UnityEngine;

namespace _System.Settings
{
    /// <summary>
    /// Single source of truth for life-steal tuning plus the status-effect VFX prefabs. Edited as an
    /// asset (Resources/Settings) and resolved as a singleton via <see cref="CpSettings{T}.I"/>.
    /// Burn/Stun/Slow/Knockback numeric tuning moved to <c>EffectTypeConfigSO</c> (see
    /// <c>EffectTypeConfigAuthoring</c>) — this SO no longer carries it.
    ///
    /// The ECS side does not read this SO directly (Burst can't touch managed objects): the authorings
    /// (<see cref="ActiveEffectsConfigAuthoring"/>, <see cref="LifeStealConfigAuthoring"/>) bake its
    /// values into blittable/managed singleton components (ActiveEffectsVfxConfig, LifeStealConfig) —
    /// rebake the SC_Entity_Core subscene after changing values here.
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

        [Header("Status Effect VFX")]
        [SerializeField] private GameObject _burnEffectPrefab;
        [SerializeField] private GameObject _stunEffectPrefab;
        [SerializeField] private GameObject _slowEffectPrefab;

        public GameObject BurnEffectPrefab => _burnEffectPrefab;
        public GameObject StunEffectPrefab => _stunEffectPrefab;
        public GameObject SlowEffectPrefab => _slowEffectPrefab;
    }
}
