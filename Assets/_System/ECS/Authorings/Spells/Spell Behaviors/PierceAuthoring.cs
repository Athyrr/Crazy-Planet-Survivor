using Unity.Entities;
using UnityEngine;

public class PierceAuthoring : MonoBehaviour
{
    class Baker : Baker<PierceAuthoring>
    {
        public override void Bake(PierceAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent<Pierce>(entity);
        }
    }
}
