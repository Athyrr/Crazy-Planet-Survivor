using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class MovementAuthoring : MonoBehaviour
{
    public float Speed = 1.0f;

    private class Baker : Baker<MovementAuthoring>
    {
        public override void Bake(MovementAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new Velocity()
            {
                Direction = new float3(0, 0, 1),
                Magnitude = authoring.Speed
            });
        }
    }
}
