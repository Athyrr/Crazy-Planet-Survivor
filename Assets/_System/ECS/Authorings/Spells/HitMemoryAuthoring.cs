using Unity.Entities;
using UnityEngine;

public class HitMemoryAuthoring : MonoBehaviour
{
    class Baker : Baker<HitMemoryAuthoring>
    {
        public override void Bake(HitMemoryAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent<HitEntityMemory>(entity);
        }
    }
}
