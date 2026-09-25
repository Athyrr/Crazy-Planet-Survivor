using UnityEngine;
using Unity.Entities;

/// <summary>
/// Authoring component for active effects on an entity. This is used to add the necessary components for active effects, which can then be enabled/disabled by the Active Effects System .
/// </summary>
public class ActiveEffectsAuthoring : MonoBehaviour
{
    // todo handle immunities
    
    private class Baker : Baker<ActiveEffectsAuthoring>
    {
        public override void Bake(ActiveEffectsAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.None);

            AddBuffer<StatusEffectInstance>(entity);
            AddBuffer<StatusEffectApplyRequest>(entity);

            AddComponent<SlowState>(entity);
            SetComponentEnabled<SlowState>(entity, false);

            AddComponent<StunState>(entity);
            SetComponentEnabled<StunState>(entity, false);

            AddComponent<BurnState>(entity);
            SetComponentEnabled<BurnState>(entity, false);

            AddComponent<KnockbackState>(entity);
            SetComponentEnabled<KnockbackState>(entity, false);
        }
    }
}

