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

    [Header("Stun Rules")] 
    public float DefaultStunDuration = 1.5f;

    
    [Header("Slow Rules")]
    
    [Tooltip(("Multiplier applied to the target's speed." +
              "\n0.5f = -50% speed."))]
    public float DefaultSlowMultiplier = 0.3f;

    public float DefaultSlowDuration = 2.0f;

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

                StunBaseDuration = authoring.DefaultStunDuration,

                BaseSlowMultiplier = authoring.DefaultSlowMultiplier,
                SlowDuration = authoring.DefaultSlowDuration,
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
    public float StunBaseDuration;

    // Slow
    public float BaseSlowMultiplier;
    public float SlowDuration;
}