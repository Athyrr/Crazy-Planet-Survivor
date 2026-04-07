using _System.ECS.Components.Entity;
using Unity.Entities;
using UnityEngine;

public class DestructibleAuthoring : MonoBehaviour
{
    
    private class Baker : Baker<DestructibleAuthoring>
    {
        public override void Bake(DestructibleAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);
            
            AddComponent(entity, new Destructible());
            
            AddComponent(entity, new DestroyEntityFlag());
            SetComponentEnabled<DestroyEntityFlag>(entity, false);
        }
    }
}
