using _System.Settings;
using Unity.Entities;
using UnityEngine;

/// <summary>
/// Bakes the enemy-debuff rules (burn/stun/slow/knockback) and the status-effect VFX prefabs from the
/// shared <see cref="CpCombatEffectsSettings"/> SO into the <see cref="ActiveEffectsConfig"/> and
/// <see cref="ActiveEffectsVfxConfig"/> singletons. The SO is the single source of truth; rebake
/// SC_Entity_Core after changing its values. Life steal lives in <see cref="LifeStealConfigAuthoring"/>.
/// </summary>
public class ActiveEffectsConfigAuthoring : MonoBehaviour
{
    [Tooltip("Shared combat effects settings SO. The debuff rules + VFX prefabs are baked from it.")]
    public CpCombatEffectsSettings Settings;

    private class Baker : Baker<ActiveEffectsConfigAuthoring>
    {
        public override void Bake(ActiveEffectsConfigAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);

            var s = authoring.Settings;
            if (s != null)
                DependsOn(s);

            // Fall back to the design defaults if the SO is unassigned, so the bake never zeroes out.
            AddComponent(entity, new ActiveEffectsConfig
            {
                BurnDamageRatio = s != null ? s.BurnDamageRatio : 0.125f,
                BurnDuration = s != null ? s.BurnDuration : 3f,
                BurnTickRate = s != null ? s.BurnTickRate : 0.3f,

                StunDuration = s != null ? s.StunDuration : 1.5f,

                BaseSlowMultiplier = s != null ? s.SlowMultiplier : 2f,
                SlowDuration = s != null ? s.SlowDuration : 3f,

                KnockbackForce = s != null ? s.KnockbackForce : 50f,
                KnockbackDuration = s != null ? s.KnockbackDuration : 0.5f,
            });

            AddComponentObject(entity, new ActiveEffectsVfxConfig
            {
                BurnEffectPrefab = s != null ? s.BurnEffectPrefab : null,
                StunEffectPrefab = s != null ? s.StunEffectPrefab : null,
                SlowEffectPrefab = s != null ? s.SlowEffectPrefab : null,
            });
        }
    }
}

public struct ActiveEffectsConfig : IComponentData
{
    // Burn
    public float BurnDamageRatio;
    public float BurnDuration;
    public float BurnTickRate;

    // Stun
    public float StunDuration;

    // Slow
    public float BaseSlowMultiplier;
    public float SlowDuration;

    // Knockback
    public float KnockbackForce;
    public float KnockbackDuration;
}

public class ActiveEffectsVfxConfig : IComponentData
{
    public GameObject BurnEffectPrefab;
    public GameObject StunEffectPrefab;
    public GameObject SlowEffectPrefab;
}