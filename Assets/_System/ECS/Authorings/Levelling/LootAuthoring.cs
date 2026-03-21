using Unity.Entities;
using UnityEngine;

public class LootAuthoring : MonoBehaviour
{
    [Min(0)]
    public int Value;

    [Range(0f, 1f)]
    public float DropChance = 1.0f;

    private class Baker : Baker<LootAuthoring>
    {
        public override void Bake(LootAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);
            AddComponent(entity, new Loot
            {
                Value = authoring.Value,
                DropChance = authoring.DropChance
            });
        }
    }
}
