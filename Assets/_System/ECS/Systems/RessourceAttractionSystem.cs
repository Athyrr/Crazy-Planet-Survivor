using _System.ECS.Components.Audio;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Manages the lifecycle of any ressources / xp orbs, handling both the detection of nearby orbs (attraction)
/// and the final collection when they reach the player.
/// Uses a throttled scanning approach to minimize performance impact.
/// Orbs are identified by LootTag and differentiated by ExperienceOrb (XP) vs Resource (material) components.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[BurstCompile]
public partial struct RessourceAttractionSystem : ISystem
{
    [ReadOnly]
    private ComponentLookup<LocalTransform> _transformLookup;

    [ReadOnly]
    private ComponentLookup<CoreStats> _statsLookup;

    [ReadOnly]
    private ComponentLookup<PlanetData> _planetLookup;

    [ReadOnly]
    private ComponentLookup<ExperienceOrb> _experienceOrbLookup;

    [ReadOnly]
    private ComponentLookup<Resource> _resourceLookup;
    private ComponentLookup<SoundPlayerTag> _soundPlayerLookup;

    private EntityQuery _playerQuery;

    private float _timeSinceLastScan;

    private const float ScanInterval = 0.25f;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameState>();
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        state.RequireForUpdate<Player>();
        state.RequireForUpdate<PlanetData>();
        state.RequireForUpdate<AttractionAnimationCurveConfig>();

        _transformLookup = state.GetComponentLookup<LocalTransform>(isReadOnly: true);
        _statsLookup = state.GetComponentLookup<CoreStats>(isReadOnly: true);
        _planetLookup = state.GetComponentLookup<PlanetData>(isReadOnly: true);
        _experienceOrbLookup = state.GetComponentLookup<ExperienceOrb>(isReadOnly: true);
        _resourceLookup = state.GetComponentLookup<Resource>(isReadOnly: true);
        _soundPlayerLookup = state.GetComponentLookup<SoundPlayerTag>(isReadOnly: false);

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
        _experienceOrbLookup.Update(ref state);
        _resourceLookup.Update(ref state);

        if (_playerQuery.IsEmptyIgnoreFilter)
            return;

        float deltaTime = SystemAPI.Time.DeltaTime;

        var config = SystemAPI.GetSingleton<AttractionAnimationCurveConfig>();
        if (!config.CurveBlobRef.IsCreated)
            return;

        var playerEntity = _playerQuery.GetSingletonEntity();

        var planetCenter = SystemAPI.GetSingleton<PlanetData>().Center;

        var ecbSingleton =
            SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
        var ecbParallel = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter();
        var resourceBuffer = SystemAPI.GetBufferLookup<ResourceBufferElement>(isReadOnly: false);
        var playerExperienceLookup = SystemAPI.GetComponentLookup<PlayerExperience>(
            isReadOnly: false
        );

        _soundPlayerLookup.Update(ref state);
        var soundPlayerEntity = SystemAPI.TryGetSingletonEntity<SoundPlayerTag>(out var spe)
            ? spe
            : Entity.Null;

        // Move collection runs first so orbs are collected before new attraction
        // Single-threaded: directly modifies the Player's ResourceBuffer and PlayerExperience
        var moveAndCollectJob = new MoveAndCollectLootJob
        {
            ECB = ecb,
            PlayerEntity = playerEntity,
            TransformLookup = _transformLookup,
            PlanetCenter = planetCenter,
            DeltaTime = deltaTime,
            CurveBlobRef = config.CurveBlobRef,
            ExperienceOrbLookup = _experienceOrbLookup,
            ResourceLookup = _resourceLookup,
            ResourceBuffer = resourceBuffer,
            PlayerExperienceLookup = playerExperienceLookup,
            SoundPlayerLookup = _soundPlayerLookup,
            SoundPlayerEntity = soundPlayerEntity,
        };
        state.Dependency = moveAndCollectJob.Schedule(state.Dependency);

        // Attraction scan (throttled)
        _timeSinceLastScan += SystemAPI.Time.DeltaTime;

        if (!(_timeSinceLastScan >= ScanInterval))
            return;

        _timeSinceLastScan = 0;

        _statsLookup.Update(ref state);
        _planetLookup.Update(ref state);

        ref var curveData = ref config.CurveBlobRef.Value;

        var attractJob = new AttractLootJob
        {
            ECB = ecbParallel,
            PlayerEntity = playerEntity,
            TransformLookup = _transformLookup,
            StatsLookup = _statsLookup,
            AnimDuration = curveData.Duration,
        };
        state.Dependency = attractJob.ScheduleParallel(state.Dependency);
    }

    /// <summary>
    /// Scans for caca caca caca orbs within the player's collection range and attaches
    /// a movement component to pull them toward the player.
    /// </summary>
    [BurstCompile]
    [WithAll(typeof(LootTag))]
    [WithNone(typeof(AttractionAnimation))]
    private partial struct AttractLootJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter ECB;
        public Entity PlayerEntity;

        [ReadOnly]
        public ComponentLookup<LocalTransform> TransformLookup;

        [ReadOnly]
        public ComponentLookup<CoreStats> StatsLookup;
        public float AnimDuration;

        private void Execute(
            [ChunkIndexInQuery] int chunkIndex,
            Entity entity,
            in LocalTransform transform
        )
        {
            var playerStats = StatsLookup[PlayerEntity];
            var finalCollectRange = playerStats.BasePickupRange * (1f + playerStats.PickupRange);

            var playerPosition = TransformLookup[PlayerEntity].Position;
            var distSq = math.distancesq(playerPosition, transform.Position);
            if (distSq <= finalCollectRange * finalCollectRange)
            {
                ECB.AddComponent(
                    chunkIndex,
                    entity,
                    new AttractionAnimation
                    {
                        StartPosition = transform.Position,
                        ElapsedTime = 0f,
                        Duration = AnimDuration,
                    }
                );
            }
        }
    }

    /// <summary>
    /// Moves attracted orbs toward the player along an animation curve.
    /// On arrival, rewards XP or resources (determined by component type)
    /// and marks the orb for destruction.
    /// </summary>
    [BurstCompile]
    [WithAll(typeof(LootTag))]
    private partial struct MoveAndCollectLootJob : IJobEntity
    {
        public EntityCommandBuffer ECB;
        public float DeltaTime;

        public Entity PlayerEntity;

        [NativeDisableContainerSafetyRestriction]
        [ReadOnly]
        public ComponentLookup<LocalTransform> TransformLookup;

        public float3 PlanetCenter;

        [ReadOnly]
        public ComponentLookup<ExperienceOrb> ExperienceOrbLookup;

        [ReadOnly]
        public ComponentLookup<Resource> ResourceLookup;

        [ReadOnly]
        public BlobAssetReference<AttractionAnimationCurveBlob> CurveBlobRef;
        public BufferLookup<ResourceBufferElement> ResourceBuffer;
        public ComponentLookup<PlayerExperience> PlayerExperienceLookup;

        public ComponentLookup<SoundPlayerTag> SoundPlayerLookup;
        public Entity SoundPlayerEntity;

        private void Execute(
            Entity entity,
            ref LocalTransform transform,
            ref AttractionAnimation anim
        )
        {
            var playerPosition = TransformLookup[PlayerEntity].Position;

            anim.ElapsedTime += DeltaTime;
            var progress01 = math.clamp(anim.ElapsedTime / anim.Duration, 0f, 1f);

            ref var animCurve = ref CurveBlobRef.Value;

            var sampleIndexFloat = progress01 * (animCurve.SampleCount - 1);
            var indexLower = (int)math.floor(sampleIndexFloat);
            var indexUpper = math.min(indexLower + 1, animCurve.SampleCount - 1);
            var lerpFactor = sampleIndexFloat - indexLower;

            var curveValue = math.lerp(
                animCurve.Samples[indexLower],
                animCurve.Samples[indexUpper],
                lerpFactor
            );

            // Follow the planet curvature
            var dirStart = math.normalize(anim.StartPosition - PlanetCenter);
            var dirEnd = math.normalize(playerPosition - PlanetCenter);
            var dir = math.normalize(math.lerp(dirStart, dirEnd, curveValue));

            var startRadius = math.distance(anim.StartPosition, PlanetCenter);
            var endRadius = math.distance(playerPosition, PlanetCenter);
            var radius = math.lerp(startRadius, endRadius, curveValue);

            transform.Position = PlanetCenter + dir * radius;

            if (!(progress01 >= 1f))
                return;

            if (ExperienceOrbLookup.HasComponent(entity))
            {
                // Direct increment — no buffer event needed
                if (PlayerExperienceLookup.TryGetComponent(PlayerEntity, out var exp))
                {
                    exp.Experience += ExperienceOrbLookup[entity].Value;
                    PlayerExperienceLookup[PlayerEntity] = exp;
                }

                //fire next frame.
                if (
                    SoundPlayerEntity != Entity.Null
                    && SoundPlayerLookup.HasComponent(SoundPlayerEntity)
                )
                {
                    var soundTag = SoundPlayerLookup[SoundPlayerEntity];
                    soundTag.GemsCollectedSound++;
                    SoundPlayerLookup[SoundPlayerEntity] = soundTag;
                }
            }
            else if (ResourceLookup.HasComponent(entity))
            {
                var resource = ResourceLookup[entity];
                if (ResourceBuffer.HasBuffer(PlayerEntity))
                {
                    var buffer = ResourceBuffer[PlayerEntity];
                    bool found = false;
                    for (int i = 0; i < buffer.Length; i++)
                    {
                        if (buffer[i].Type == resource.Type)
                        {
                            buffer[i] = new ResourceBufferElement
                            {
                                Type = resource.Type,
                                Value = buffer[i].Value + 1,
                            };
                            found = true;
                            break;
                        }
                    }

                    if (!found)
                    {
                        buffer.Add(new ResourceBufferElement { Type = resource.Type, Value = 1 });
                    }
                }
            }

            ECB.SetComponentEnabled<DestroyEntityFlag>(entity, true);
        }
    }
}
