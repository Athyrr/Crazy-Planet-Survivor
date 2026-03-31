using Unity.Entities;
using UnityEngine;

public class ActiveEffectsConfigAuthoring : MonoBehaviour
{
    [Header("Burn Rules")]
    [Tooltip("Multiplier applied to the spell's damage to calculate burn damage. " +
             "\n0.3 = Burn deals 30% of the spell damage.")]
    public float DefaultBurnDamageRatio = 5f;

    public float DefaultBurnDuration = 3f;

    [Tooltip("How often the burn damage is applied." +
             "\n0.5f = every 0.5 seconds.")]
    public float DefaultBurnTickRate = 1f;

    public GameObject BurnEffectPrefab;

    [Header("Stun Rules")] public float DefaultStunDuration = 1.5f;
    public GameObject StunEffectPrefab;

    [Header("Slow Rules")]
    [Tooltip(("Multiplier applied to the target's speed." +
              "\n0.5f = -50% speed."))]
    public float DefaultSlowMultiplier = 0.3f;

    public float DefaultSlowDuration = 2.0f;
    public GameObject SlowEffectPrefab;

    [Header("Knockback Rules")] public float DefaultKnockbackForce = 15f;
    public float DefaultKnockbackDuration = 0.3f;

    private class Baker : Baker<ActiveEffectsConfigAuthoring>
    {
        public override void Bake(ActiveEffectsConfigAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);

            AddComponent(entity, new ActiveEffectsConfig
            {
                BurnDamageRatio = authoring.DefaultBurnDamageRatio,
                BurnDuration = authoring.DefaultBurnDuration,
                BurnTickRate = authoring.DefaultBurnTickRate,

                StunDuration = authoring.DefaultStunDuration,

                BaseSlowMultiplier = authoring.DefaultSlowMultiplier,
                SlowDuration = authoring.DefaultSlowDuration,

                KnockbackForce = authoring.DefaultKnockbackForce,
                KnockbackDuration = authoring.DefaultKnockbackDuration,
            });

            AddComponentObject(entity, new ActiveEffectsVfxConfig
            {
                BurnEffectPrefab = authoring.BurnEffectPrefab,
                StunEffectPrefab = authoring.StunEffectPrefab,
                SlowEffectPrefab = authoring.SlowEffectPrefab,
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