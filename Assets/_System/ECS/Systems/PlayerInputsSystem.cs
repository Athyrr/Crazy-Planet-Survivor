using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;

[UpdateInGroup(typeof(InitializationSystemGroup))]
[BurstCompile]
public partial struct PlayerInputSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Player>();
        state.RequireForUpdate<PlanetData>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var inputData = SystemAPI.GetSingleton<InputData>();
        float2 inputVector = inputData.Value;

        if (math.lengthsq(inputVector) < 0.01f)
        {
            foreach (var movement in SystemAPI.Query<RefRW<LinearMovement>>().WithAll<Player>())
            {
                movement.ValueRW.Direction = float3.zero;
            }
            return;
        }

        foreach (var (transform, movement) in
                 SystemAPI.Query<RefRO<LocalTransform>, RefRW<LinearMovement>>().WithAll<Player>())
        {
            // Get planet data
            Entity planetEntity = SystemAPI.GetSingletonEntity<PlanetData>();
            float3 planetCenter = SystemAPI.GetComponent<LocalTransform>(planetEntity).Position;

            float3 playerPos = transform.ValueRO.Position;
            float3 normal = math.normalize(playerPos - planetCenter);

            // Convert input to world direction using camera
            float3 worldDirection = CalculateWorldDirection(inputVector, normal, ref state);

            movement.ValueRW.Direction = worldDirection;
        }
    }

    [BurstCompile]
    private float3 CalculateWorldDirection(float2 input, float3 surfaceNormal, ref SystemState state)
    {
        if (SystemAPI.TryGetSingleton<CameraWorldData>(out var cameraData))
        {
            float3 cameraRight = cameraData.Right;
            float3 cameraForward = cameraData.Forward;

            // Project on surface 
            cameraRight = cameraRight - math.dot(cameraRight, surfaceNormal) * surfaceNormal;
            cameraForward = cameraForward - math.dot(cameraForward, surfaceNormal) * surfaceNormal;

            cameraRight = math.normalize(cameraRight);
            cameraForward = math.normalize(cameraForward);

            // Camera related direction
            float3 worldDirection = cameraRight * input.x + cameraForward * input.y;
            return math.normalize(worldDirection);
        }
        else
        {
            float3 worldRight = new float3(1, 0, 0);
            float3 worldForward = new float3(0, 0, 1);

            worldRight = worldRight - math.dot(worldRight, surfaceNormal) * surfaceNormal;
            worldForward = worldForward - math.dot(worldForward, surfaceNormal) * surfaceNormal;

            worldRight = math.normalize(worldRight);
            worldForward = math.normalize(worldForward);

            // World related direction
            float3 direction = worldRight * input.x + worldForward * input.y;
            return math.normalize(direction);
        }
    }
}
