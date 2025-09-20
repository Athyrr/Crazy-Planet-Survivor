using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

public class PlayerAuthoring : MonoBehaviour
{
    [Header("Movement Settings")]
    public float Speed = 1.0f;

    private class Baker : Baker<PlayerAuthoring>
    {
        public override void Bake(PlayerAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent(entity, new LocalTransform()
            {
                Position = authoring.transform.position,
                Rotation = authoring.transform.rotation,
                Scale = 1
            });
            AddComponent(entity, new Player() { });
            AddComponent(entity, new LinearMovement()
            {
                Direction = float3.zero,
                Speed = authoring.Speed
            });
            AddComponent(entity, new InputData() { Value = new float2(0, 0) });
        }
    }
}
