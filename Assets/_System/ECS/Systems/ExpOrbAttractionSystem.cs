using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;
using Unity.Collections;
using Unity.Mathematics;

/// <summary>
/// Manages the lifecycle of experience orbs, handling both the detection of nearby orbs (attraction) 
/// and the final collection when they reach the player. 
/// Uses a throttled scanning approach to minimize performance impact.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[BurstCompile]
public partial struct ExpOrbAttractionSystem : ISystem
{
    /// <summary> Cached lookup for world positions. </summary>
    [ReadOnly] private ComponentLookup<LocalTransform> _transformLookup;
    /// <summary> Cached lookup for player stats (Collection Range/Speed). </summary>
    [ReadOnly] private ComponentLookup<Stats> _statsLookup;
    /// <summary> Cached lookup for planet-specific data. </summary>
    [ReadOnly] private ComponentLookup<PlanetData> _planetLookup;

    private float _timeSinceLastScan;
    /// <summary> Frequency of the attraction scan (4 times per second). </summary>
    private const float SCAN_INTERVAL = 0.25f; 

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
        // Refresh transform lookup for the current frame
        _transformLookup.Update(ref state);

        // --- PHASE 1: Collection (Fast Path) ---
        // This runs every frame to ensure that orbs disappear immediately upon touching the player.
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

            // --- PHASE 2: Attraction (Slow Path / Throttled) ---
            // Scanning for new orbs is computationally expensive. Throttling this to 4Hz 
            // significantly reduces CPU overhead without impacting perceived gameplay quality.
            _timeSinceLastScan += SystemAPI.Time.DeltaTime;
            if (_timeSinceLastScan >= SCAN_INTERVAL)
            {
                _timeSinceLastScan = 0;
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

    /// <summary>
    /// Scans for static orbs within the player's collection range and attaches 
    /// a movement component to pull them toward the player.
    /// </summary>
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

            // Optimization: Use Squared Euclidean Distance.
            // At the ranges used for collection, the difference between a straight line 
            // and the planet's arc is negligible, but the performance gain is significant.
            float distSq = math.distancesq(playerPos, transform.Position);

            // If within range, convert the static orb into a following entity
            if (distSq <= collectRange * collectRange)
            {
                ECB.AddComponent(chunkIndex, entity, new FollowTargetMovement()
                {
                    Target = PlayerEntity,
                    Speed = playerSpeed * 1.75f,
                    StopDistance = 1f
                });
            }
        }
    }

    /// <summary>
    /// Checks if an attracting orb has reached the player. 
    /// If so, it rewards experience and destroys the orb.
    /// </summary>
    [BurstCompile]
    private partial struct CollectOrbJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter ECB;
        public Entity PlayerEntity;
        [ReadOnly] public ComponentLookup<LocalTransform> TransformLookup;

        public void Execute([ChunkIndexInQuery] int chunkIndex, in Entity entity, in ExperienceOrb orb, in LocalTransform transform, in FollowTargetMovement followMovement)
        {
            if (!TransformLookup.HasComponent(PlayerEntity)) return;

            float3 playerPos = TransformLookup[PlayerEntity].Position;
            float distSq = math.distancesq(playerPos, transform.Position);

            // Check if the orb is close enough to be considered "collected"
            if (distSq <= followMovement.StopDistance * followMovement.StopDistance)
            {
                // Add experience to the player's buffer and flag the orb for removal
                ECB.AppendToBuffer(chunkIndex, PlayerEntity, new CollectedExperienceBufferElement()
                {
                    Value = orb.Value
                });
                ECB.AddComponent<DestroyEntityFlag>(chunkIndex, entity);
            }
        }
    }
}