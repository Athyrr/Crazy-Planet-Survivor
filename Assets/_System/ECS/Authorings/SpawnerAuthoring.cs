using Unity.Entities;
using UnityEngine;

public class SpawnerAuthoring : MonoBehaviour
{
    public GameObject prefab;

    public int amount = 0;

    private class Baker : Baker<SpawnerAuthoring>
    {
        public override void Bake(SpawnerAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);

            AddComponent(entity, new SpawnConfig()
            {
                Prefab = GetEntity(authoring.prefab, TransformUsageFlags.Dynamic),
                Amount = authoring.amount,
            });
        }
    }
}
