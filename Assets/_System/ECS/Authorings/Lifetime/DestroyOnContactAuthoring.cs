using Unity.Entities;
using UnityEngine;

public class DestroyOnContactAuthoring : MonoBehaviour
{
    class Baker : Baker<DestroyOnContactAuthoring>
    {
        public override void Bake(DestroyOnContactAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent<DestroyOnContact>(entity);
        }
    }
}
