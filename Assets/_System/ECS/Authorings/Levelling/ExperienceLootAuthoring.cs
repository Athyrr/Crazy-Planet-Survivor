using Unity.Entities;
using UnityEngine;

public class ExperienceLootAuthoring : MonoBehaviour
{
    [Min(0)]
    public int ExperienceValue;

    [Range(0f, 1f)]
    public float DropChance = 1.0f;

    private class Baker : Baker<ExperienceLootAuthoring>
    {
        public override void Bake(ExperienceLootAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);
            AddComponent(entity, new ExperienceLoot
            {
                ExperienceValue = authoring.ExperienceValue,
                DropChance = authoring.DropChance
            });
        }
    }
}
