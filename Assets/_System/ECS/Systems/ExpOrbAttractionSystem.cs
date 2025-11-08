using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;
using Unity.Collections;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[BurstCompile]
public partial struct ExpOrbAttractionSystem : ISystem
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
        if (!SystemAPI.TryGetSingleton<GameState>(out var gameState))
            return;

        if (gameState.State != EGameState.Running)
            return;

        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter();

        if (!SystemAPI.TryGetSingletonEntity<Player>(out var playerEntity))
            return;

        if (!SystemAPI.TryGetSingletonEntity<PlanetData>(out var planetEntity))
            return;

        var planetTansform = SystemAPI.GetComponent<LocalTransform>(planetEntity);
        var planetRadius = SystemAPI.GetComponentRO<PlanetData>(planetEntity).ValueRO.Radius;

        LocalTransform playerTransform = SystemAPI.GetComponent<LocalTransform>(playerEntity);
        float playerCollectRange = SystemAPI.GetComponentRO<Stats>(playerEntity).ValueRO.CollectRange;
        float playerSpeed = SystemAPI.GetComponentRO<Stats>(playerEntity).ValueRO.MoveSpeed;

        var attractJob = new AttractOrbJob()
        {
            ECB = ecb,
            PlayerEntity = playerEntity,
            PlayerTransform = playerTransform,
            CollectRange = playerCollectRange,
            PlayerSpeed = playerSpeed,
            PlanetRadius = planetRadius,
            PlanetTransform = planetTansform
        };
        var attractJobHandle = attractJob.ScheduleParallel(state.Dependency);

        var collectJob = new CollectOrbJob()
        {
            ECB = ecb,
            PlayerEntity = playerEntity,
            PlayerTransform = playerTransform,
            CollectRange = playerCollectRange,

            //PlanetRadius = planetRadius,
            //PlanetTransform = planetTansform
        };
        state.Dependency = collectJob.ScheduleParallel(attractJobHandle);
    }

    [BurstCompile]
    private partial struct CollectOrbJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter ECB;

        [ReadOnly] public Entity PlayerEntity;
        [ReadOnly] public LocalTransform PlayerTransform;
        [ReadOnly] public float CollectRange;

        //[ReadOnly] public LocalTransform PlanetTransform;
        //[ReadOnly] public float PlanetRadius;

        public void Execute([ChunkIndexInQuery] int chunkIndex, in Entity entity, in ExperienceOrb orb, ref LocalTransform transform, in FollowTargetMovement followMovement)
        {
            PlanetMovementUtils.GetDistanceEuclidean(PlayerTransform.Position, transform.Position, out var dist);
            if (dist > followMovement.StopDistance)
                return;

            ECB.AppendToBuffer(chunkIndex, PlayerEntity, new CollectedExperienceBufferElement()
            {
                Value = orb.Value
            });

            ECB.AddComponent<DestroyEntityFlag>(chunkIndex, entity);
        }
    }

    [WithNone(typeof(FollowTargetMovement))]
    [BurstCompile]
    private partial struct AttractOrbJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter ECB;

        [ReadOnly] public Entity PlayerEntity;
        [ReadOnly] public LocalTransform PlayerTransform;
        [ReadOnly] public float CollectRange;
        [ReadOnly] public float PlayerSpeed;

        [ReadOnly] public LocalTransform PlanetTransform;
        [ReadOnly] public float PlanetRadius;

        public void Execute([ChunkIndexInQuery] int chunkIndex, in Entity entity, in ExperienceOrb orb, ref LocalTransform transform)
        {
            PlanetMovementUtils.GetSurfaceDistanceRadius(PlayerTransform.Position, transform.Position, PlanetTransform.Position, PlanetRadius, out var dist);

            if (dist > CollectRange)
                return;

            ECB.AddComponent(chunkIndex, entity, new FollowTargetMovement()
            {
                Target = PlayerEntity,
                Speed = PlayerSpeed * 1.25f,
                StopDistance = 1f
            });
        }
    }
}
