using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial struct SelfRotateSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<SelfRotate>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // Get game state
        if (!SystemAPI.TryGetSingleton<GameState>(out var gameState))
            return;

        // Only run when game is running
        if (gameState.State != EGameState.Running)
            return;

        if (!SystemAPI.TryGetSingletonEntity<PlanetData>(out var planetEntity))
            return;

        var planetTransform = SystemAPI.GetComponentRO<LocalTransform>(planetEntity).ValueRO;

        float deltaTime = SystemAPI.Time.DeltaTime;

        var selfRotateJob = new SelfRotateJob
        {
            DeltaTime = SystemAPI.Time.DeltaTime,
            PlanetCenter = planetTransform.Position
        };
        var handle = selfRotateJob.ScheduleParallel(state.Dependency);

        state.Dependency = handle;
    }

    [BurstCompile]
    private partial struct SelfRotateJob : IJobEntity
    {
        [ReadOnly] public float DeltaTime;

        [ReadOnly] public float3 PlanetCenter;

        public void Execute([ChunkIndexInQuery] int chunkIndex, Entity entity, in SelfRotate selfRotate, ref LocalTransform localTransform)
        {
            //PlanetMovementUtils.GetSurfaceNormalRadius(localTransform.Position, PlanetCenter, out var surfaceNormal);
            //float3 surfaceNormal = math.normalize(localTransform.Position - PlanetCenter);

            //float angle = math.radians(selfRotate.RotationSpeed) * DeltaTime;
            //quaternion rot = quaternion.AxisAngle(surfaceNormal, angle);

            //float3 currentForward = localTransform.Forward();
            //float3 newForward = math.rotate(rot, currentForward);

            //localTransform.Rotation = quaternion.LookRotationSafe(newForward, surfaceNormal);



            float3 surfaceNormal = math.normalize(localTransform.Position - PlanetCenter);

            float3 currentForward = localTransform.Forward();
            float3 tangentForward = math.normalize(currentForward - math.dot(currentForward, surfaceNormal) * surfaceNormal);

            quaternion alignedRotation = quaternion.LookRotationSafe(tangentForward, surfaceNormal);

            float angle = math.radians(selfRotate.RotationSpeed) * DeltaTime;

            localTransform.Rotation = math.mul(alignedRotation, quaternion.RotateY(angle));
        }
    }
}