using Unity.Transforms;
using Unity.Entities;
using UnityEngine;

public class ChildEntitiesSpawnerAuthoring : MonoBehaviour
{
    private class Baker : Baker<ChildEntitiesSpawnerAuthoring>
    {
        public override void Bake(ChildEntitiesSpawnerAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            AddBuffer<Child>(entity);

            AddComponent<ChildEntitiesSpawner>(entity);
        }
    }
}
