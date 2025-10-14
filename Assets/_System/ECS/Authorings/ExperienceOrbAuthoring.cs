using UnityEngine;
using Unity.Entities;

public class ExperienceOrbAuthoring : MonoBehaviour
{
    public int Value;
    private class Baker : Baker<ExperienceOrbAuthoring>
    {
        public override void Bake(ExperienceOrbAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new ExperienceOrb { Value = authoring.Value });
        }
    }
}
