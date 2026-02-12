using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;
using Unity.Collections;
using Unity.Mathematics;
using System.Runtime.InteropServices.WindowsRuntime;

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

    private EntityQuery _playerQuery;

    private float _timeSinceLastScan;
    /// <summary> Frequency of the attraction scan (4 times per second). </summary>
    private const float SCAN_INTERVAL = 0.25f;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Player>();
        state.RequireForUpdate<PlanetData>();
        state.RequireForUpdate<ExpAttractionAnimationCurveConfig>();

        _transformLookup = state.GetComponentLookup<LocalTransform>(isReadOnly: true);
        _statsLookup = state.GetComponentLookup<Stats>(isReadOnly: true);
        _planetLookup = state.GetComponentLookup<PlanetData>(isReadOnly: true);

        //_playerQuery = state.GetEntityQuery(typeof(Player));
        ComponentType playerComponentType = ComponentType.ReadOnly<Player>();
        _playerQuery = state.GetEntityQuery(playerComponentType);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var gameStateEntity = SystemAPI.GetSingletonEntity<GameState>();
        var gameState = SystemAPI.GetComponent<GameState>(gameStateEntity);
        if (gameState.State != EGameState.Running)
            return;

        _transformLookup.Update(ref state);

        if (_playerQuery.IsEmptyIgnoreFilter)
            return;

        float deltaTime = SystemAPI.Time.DeltaTime;

        var config = SystemAPI.GetSingleton<ExpAttractionAnimationCurveConfig>();
        if (!config.CurveBlobRef.IsCreated)
            return;

        //float3 playerPosition = _playerQuery.GetSingleton<LocalTransform>().Position;
        var playerEntity = _playerQuery.GetSingletonEntity();
        var playerPosition = SystemAPI.GetComponent<LocalTransform>(playerEntity).Position;

        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter();

        // Animation and Collection
        var moveAndcollectJob = new MoveAndCollectExpJob()
        {
            ECB = ecb,
            PlayerEntity = playerEntity,
            PlayerPosition = playerPosition,
            DeltaTime = deltaTime,
            CurveBlobRef = config.CurveBlobRef
        };
        state.Dependency = moveAndcollectJob.ScheduleParallel(state.Dependency);

        // Exp detection and Attraction 
        _timeSinceLastScan += SystemAPI.Time.DeltaTime;
        if (_timeSinceLastScan >= SCAN_INTERVAL)
        {
            _timeSinceLastScan = 0;

            _statsLookup.Update(ref state);
            _planetLookup.Update(ref state);

            ref var curveData = ref config.CurveBlobRef.Value;

            var attractJob = new AttractExpJob()
            {
                ECB = ecb,
                PlayerEntity = playerEntity,
                PlayerPosition = playerPosition,
                StatsLookup = _statsLookup,
                AnimDuration = curveData.Duration
            };
            state.Dependency = attractJob.ScheduleParallel(state.Dependency);
        }
    }

    /// <summary>
    /// Scans for static orbs within the player's collection range and attaches 
    /// a movement component to pull them toward the player.
    /// </summary>
    [BurstCompile]
    [WithNone(typeof(ExpAttractionAnimation))]
    private partial struct AttractExpJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter ECB;
        public Entity PlayerEntity;

        public float3 PlayerPosition;

        [ReadOnly] public ComponentLookup<Stats> StatsLookup;
        public float AnimDuration;

        public void Execute([ChunkIndexInQuery] int chunkIndex, Entity entity, in LocalTransform transform, in ExperienceOrb orb)
        {
            float collectRange = StatsLookup[PlayerEntity].CollectRange;

            // Check if within collection range
            if (math.distancesq(PlayerPosition, transform.Position) <= collectRange * collectRange)
            {
                ECB.AddComponent(chunkIndex, entity, new ExpAttractionAnimation
                {
                    StartPosition = transform.Position,
                    ElapsedTime = 0f,
                    Duration = AnimDuration
                });
            }
        }
    }

    /// <summary>
    /// Checks if an attracting orb has reached the player. 
    /// If so, it rewards experience and destroys the orb.
    /// </summary>
    [BurstCompile]
    private partial struct MoveAndCollectExpJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter ECB;
        public float DeltaTime;
        public Entity PlayerEntity;
        public float3 PlayerPosition;

        [ReadOnly] public BlobAssetReference<ExpAttractionAnimationCurveBlob> CurveBlobRef;

        public void Execute([ChunkIndexInQuery] int chunkIndex, Entity entity, ref LocalTransform transform, ref ExpAttractionAnimation anim, in ExperienceOrb orb)
        {
            anim.ElapsedTime += DeltaTime;
            float progress01 = math.clamp(anim.ElapsedTime / anim.Duration, 0f, 1f);

            // Animation curve sampling
            ref var animCurve = ref CurveBlobRef.Value;

            // Determine sample indices
            float sampleIndexFloat = progress01 * (animCurve.SampleCount - 1);
            int indexLower = (int)math.floor(sampleIndexFloat);
            int indexUpper = math.min(indexLower + 1, animCurve.SampleCount - 1);
            float lerpFactor = sampleIndexFloat - indexLower;

            // Interpolate curve value
            float curveValue = math.lerp(animCurve.Samples[indexLower], animCurve.Samples[indexUpper], lerpFactor);

            // Lerp position
            transform.Position = math.lerp(anim.StartPosition, PlayerPosition, curveValue);

            // Collection 
            if (progress01 >= 1f)
            {
                // Add experience to player
                ECB.AppendToBuffer(chunkIndex, PlayerEntity, new CollectedExperienceBufferElement
                {
                    Value = orb.Value
                });

                // Destruction
                ECB.AddComponent<DestroyEntityFlag>(chunkIndex, entity);
            }
        }
    }
}