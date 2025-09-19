using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

public class PlayerAuthoring : MonoBehaviour
{
    private class Baker : Baker<PlayerAuthoring>
    {
        public override void Bake(PlayerAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent(entity, new Player() { });
            AddComponent(entity, new PlayerInputData() { Value = float2.zero });
            AddComponent(entity, new CameraTarget() { });
            AddComponent(entity, new LocalTransform()
            {
                Position = authoring.transform.position,
                Rotation = authoring.transform.rotation,
                Scale = 1
            });
        }
    }
}
