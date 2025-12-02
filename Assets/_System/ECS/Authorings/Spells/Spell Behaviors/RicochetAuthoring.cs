using Unity.Entities;
using UnityEngine;

public class RicochetAuthoring : MonoBehaviour
{
    class Baker : Baker<RicochetAuthoring>
    {
        public override void Bake(RicochetAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent<Ricochet>(entity);
        }
    }
}
