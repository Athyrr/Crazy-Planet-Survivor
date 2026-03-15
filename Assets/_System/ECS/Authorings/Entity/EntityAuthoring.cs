using _System.ECS.Components.Entity;
using Unity.Entities;
using UnityEngine;

public class EntityAuthoring : MonoBehaviour
{
    
    private class Baker : Baker<EntityAuthoring>
    {
        public override void Bake(EntityAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);
            
            AddComponent(entity, new CpEntity());
        }
    }
}
