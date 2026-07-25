using UnityEngine;
using Unity.Entities;

public class AvoidanceAuthoring : MonoBehaviour
{
    [Tooltip("Avoidance footprint / detection radius (personal space) in world units. This is a GAMEPLAY " +
             "size, independent of the visual mesh scale — how much room the entity claims in the horde.")]
    [SerializeField]
    private float _avoidanceRadius = 2f;

    [Tooltip("Mass. Repulsion is split by mass ratio: a heavier entity shoves lighter ones aside while " +
             "barely moving itself. Independent of the radius — a small entity can be very heavy.")]
    [SerializeField]
    private float _mass = 1f;

    private class Baker : Baker<AvoidanceAuthoring>
    {
        public override void Bake(AvoidanceAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent(entity, new Avoidance
            {
                Radius = authoring._avoidanceRadius,
                Mass = Mathf.Max(0.0001f, authoring._mass)
            });

            AddComponent<SteeringForce>(entity);
        }
    }
}
