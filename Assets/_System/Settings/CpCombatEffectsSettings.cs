using UnityEngine;

namespace _System.Settings
{
    /// <summary>
    /// Status-effect VFX prefabs (Burn/Stun/Slow). Edited as an asset (Resources/Settings) and resolved
    /// as a singleton via <see cref="CpSettings{T}.I"/>. Life-steal tuning moved to
    /// <see cref="CpEffectTypeSettings"/> — this SO no longer carries it.
    ///
    /// The ECS side does not read this SO directly (Burst can't touch managed objects): the merged
    /// EffectTypeConfigAuthoring bakes its values into a blittable/managed singleton component
    /// (ActiveEffectsVfxConfig) — rebake the SC_Entity_Core subscene after changing values here.
    /// </summary>
    [CreateAssetMenu(
        fileName = "CombatEffectsSettings",
        menuName = "CPSettings/CombatEffectsSettings")]
    public class CpCombatEffectsSettings : CpSettings<CpCombatEffectsSettings>
    {
        [Header("Status Effect VFX")]
        [SerializeField] private GameObject _burnEffectPrefab;
        [SerializeField] private GameObject _stunEffectPrefab;
        [SerializeField] private GameObject _slowEffectPrefab;

        public GameObject BurnEffectPrefab => _burnEffectPrefab;
        public GameObject StunEffectPrefab => _stunEffectPrefab;
        public GameObject SlowEffectPrefab => _slowEffectPrefab;
    }
}
