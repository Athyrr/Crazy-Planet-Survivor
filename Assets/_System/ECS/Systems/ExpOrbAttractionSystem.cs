using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;
using Unity.Collections;
using Unity.Mathematics;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[BurstCompile]
public partial struct ExpOrbAttractionSystem : ISystem
{
    // Lookups
    [ReadOnly] private ComponentLookup<LocalTransform> _transformLookup;
    [ReadOnly] private ComponentLookup<Stats> _statsLookup;
    [ReadOnly] private ComponentLookup<PlanetData> _planetLookup;

    // Throttle state
    private float _timeSinceLastScan;
    private const float SCAN_INTERVAL = 0.25f; // Run detection only 4 times per second

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Player>();
        state.RequireForUpdate<PlanetData>();

        _transformLookup = state.GetComponentLookup<LocalTransform>(isReadOnly: true);
        _statsLookup = state.GetComponentLookup<Stats>(isReadOnly: true);
        _planetLookup = state.GetComponentLookup<PlanetData>(isReadOnly: true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        _transformLookup.Update(ref state);

        // --- FAST PATH: Collection ---
        // We run "Collection" every frame because if an orb touches the player,
        // it should disappear instantly to feel responsive.
        if (SystemAPI.TryGetSingletonEntity<Player>(out var playerEntity))
        {
            var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter();

            var collectJob = new CollectOrbJob()
            {
                ECB = ecb,
                PlayerEntity = playerEntity,
                TransformLookup = _transformLookup
            };
            state.Dependency = collectJob.ScheduleParallel(state.Dependency);

            // --- SLOW PATH: Attraction (Throttled) ---
            // We only look for NEW orbs to attract every 0.25s.
            // This cuts the cost of this job by factor of ~15x (at 60fps).
            _timeSinceLastScan += SystemAPI.Time.DeltaTime;
            if (_timeSinceLastScan >= SCAN_INTERVAL)
            {
                _timeSinceLastScan = 0;

                // We need fresh lookups for this job too
                _statsLookup.Update(ref state);
                _planetLookup.Update(ref state);

                if (SystemAPI.TryGetSingletonEntity<PlanetData>(out var planetEntity))
                {
                    var attractJob = new AttractOrbJob()
                    {
                        ECB = ecb,
                        PlayerEntity = playerEntity,
                        PlanetEntity = planetEntity,
                        TransformLookup = _transformLookup,
                        StatsLookup = _statsLookup,
                        PlanetLookup = _planetLookup
                    };
                    state.Dependency = attractJob.ScheduleParallel(state.Dependency);
                }
            }
        }
    }

    [BurstCompile]
    [WithNone(typeof(FollowTargetMovement))] // Only scan static orbs
    private partial struct AttractOrbJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter ECB;

        public Entity PlayerEntity;
        public Entity PlanetEntity;

        [ReadOnly] public ComponentLookup<LocalTransform> TransformLookup;
        [ReadOnly] public ComponentLookup<Stats> StatsLookup;
        [ReadOnly] public ComponentLookup<PlanetData> PlanetLookup;

        public void Execute([ChunkIndexInQuery] int chunkIndex, in Entity entity, in ExperienceOrb orb, in LocalTransform transform)
        {
            if (!TransformLookup.HasComponent(PlayerEntity)) return;

            float3 playerPos = TransformLookup[PlayerEntity].Position;
            float collectRange = StatsLookup[PlayerEntity].CollectRange;
            float playerSpeed = StatsLookup[PlayerEntity].MoveSpeed;
            // float planetRadius = PlanetLookup[PlanetEntity].Radius; // Not strictly needed for distance check if using Euclidean

            // OPTIMIZATION: Squared Euclidean Distance.
            // Math.distancesq is significantly faster than Surface Distance (which uses acos/sqrt).
            // For attraction ranges (usually < 50m), the error vs Surface distance is < 1%.
            float distSq = math.distancesq(playerPos, transform.Position);

            if (distSq <= collectRange * collectRange)
            {
                ECB.AddComponent(chunkIndex, entity, new FollowTargetMovement()
                {
                    Target = PlayerEntity,
                    Speed = playerSpeed * 1.25f,
                    StopDistance = 1f
                });
            }
        }
    }

    [BurstCompile]
    private partial struct CollectOrbJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter ECB;
        public Entity PlayerEntity;
        [ReadOnly] public ComponentLookup<LocalTransform> TransformLookup;

        public void Execute([ChunkIndexInQuery] int chunkIndex, in Entity entity, in ExperienceOrb orb, in LocalTransform transform, in FollowTargetMovement followMovement)
        {
            // Only process orbs that are actually following something
            if (!TransformLookup.HasComponent(PlayerEntity)) return;

            float3 playerPos = TransformLookup[PlayerEntity].Position;
            float distSq = math.distancesq(playerPos, transform.Position);

            if (distSq <= followMovement.StopDistance * followMovement.StopDistance)
            {
                ECB.AppendToBuffer(chunkIndex, PlayerEntity, new CollectedExperienceBufferElement()
                {
                    Value = orb.Value
                });
                ECB.AddComponent<DestroyEntityFlag>(chunkIndex, entity);
            }
        }
    }
}