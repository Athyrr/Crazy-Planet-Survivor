using Unity.Entities;
using UnityEngine;

public class DamageBufferAuthoring : MonoBehaviour
{
    
    private class Baker : Baker<DamageBufferAuthoring>
    {
        public override void Bake(DamageBufferAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);
            
            AddBuffer<DamageBufferElement>(entity);
        }
    }
}
