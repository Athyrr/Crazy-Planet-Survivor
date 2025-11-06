using UnityEngine;
using Unity.Entities;

public class AvoidanceAuthoring : MonoBehaviour
{
    [Tooltip("The radius within which the entity will detect and avoid obstacles.")]
    [SerializeField]
    private float _avoidanceDetectionRadius = 5f;

    [Tooltip("The weight factor determining the strength of the avoidance behavior.")]
    [SerializeField]
    private float _avoidanceWeight = 1f;

    private class Baker : Baker<AvoidanceAuthoring>
    {
        public override void Bake(AvoidanceAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent(entity, new Avoidance
            {
                Radius = authoring._avoidanceDetectionRadius,
                Weight = authoring._avoidanceWeight
            });

            AddComponent<SteeringForce>(entity);
        }
    }
}
