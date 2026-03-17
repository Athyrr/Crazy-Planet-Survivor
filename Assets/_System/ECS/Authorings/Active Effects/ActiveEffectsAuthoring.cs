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

            AddComponent<BurnEffect>(entity);
            SetComponentEnabled<BurnEffect>(entity, false);
            
            AddComponent<StunEffect>(entity);
            SetComponentEnabled<StunEffect>(entity, false);
            
            AddComponent<SlowEffect>(entity);
            SetComponentEnabled<SlowEffect>(entity, false);
        }
    }
}

