using UnityEngine;
using Unity.Entities;

public class ExperienceOrbAuthoring : MonoBehaviour
{
    [Tooltip("The amount of experience this orb gives when collected.")]
    public int Value;

    [Tooltip("If true, the orb will snap perfectly to the ground when attracted.")]
    public bool HardSnapToGround;

    private class Baker : Baker<ExperienceOrbAuthoring>
    {
        public override void Bake(ExperienceOrbAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent(entity, new ExperienceOrb { Value = authoring.Value });

            AddComponent(entity, new RunScope());

            if (authoring.HardSnapToGround)
                AddComponent<HardSnappedMovement>(entity);
        }
    }
}
