using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine.Rendering.Universal;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[BurstCompile]
public partial struct PlayerMovementRequestProviderSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PlayerInputData>();
        state.RequireForUpdate<Player>();
        state.RequireForUpdate<PlanetData>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingletonEntity<Player>(out Entity playerEntity))
            return;

        if (!SystemAPI.TryGetSingletonEntity<PlanetData>(out Entity planetEntity))
            return;

        LocalTransform transform = SystemAPI.GetComponentRO<LocalTransform>(playerEntity).ValueRO;
        float3 position = transform.Position;
        PlayerInputData input = SystemAPI.GetComponentRO<PlayerInputData>(playerEntity).ValueRO;
        LocalTransform planetTransform = SystemAPI.GetComponentRO<LocalTransform>(planetEntity).ValueRO;
        float3 planetCenter = planetTransform.Position;

        if (math.lengthsq(input.Value) < 0.01f)
            return;

        float3 normal = math.normalize(position - planetCenter);
        float3 movementDirection = float3.zero;

        if (SystemAPI.TryGetSingleton<CameraWorldData>(out var cameraData))
        {
            float3 cameraRight = cameraData.Right;
            float3 cameraForward = cameraData.Forward;

            cameraRight = math.normalize(cameraRight - math.dot(cameraRight, normal) * normal);
            cameraForward = math.normalize(cameraForward - math.dot(cameraForward, normal) * normal);

            movementDirection = cameraRight * input.Value.x + cameraForward * input.Value.y;
        }

        movementDirection = math.normalize(movementDirection);

        var ecbSystem = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        EntityCommandBuffer ecb = ecbSystem.CreateCommandBuffer(state.WorldUnmanaged);
        ecb.AddComponent(playerEntity, new RequestForMovement { Direction = movementDirection });
    }
}
