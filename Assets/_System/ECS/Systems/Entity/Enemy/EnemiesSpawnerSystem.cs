using Unity.Collections;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Entities;
using Unity.Physics;
using Unity.Burst;
using Unity.Jobs;

/// <summary>
/// Handles the logic for spawning enemies in waves on a spherical planet surface.
/// This system manages wave timing, processes pending spawn queues across multiple frames to prevent 
/// performance spikes, and calculates spawn positions based on various geometric modes.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(PlayerSpawnerSystem))]
[BurstCompile]
public partial struct EnemiesSpawnerSystem : ISystem
{
    // Queries
    //private EntityQuery _playerQuery;

    /// <summary>
    /// Limits the number of entities instantiated in a single frame to maintain a stable frame rate.
    /// </summary>
    private const int MAX_SPAWNS_PER_FRAME = 50;

    //todo allocate Ms budget et dispacth into frames

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PhysicsWorldSingleton>();
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        //state.RequireForUpdate<StartRunRequest>();
        state.RequireForUpdate<SpawnerSettings>();
        state.RequireForUpdate<SpawnerState>();
        state.RequireForUpdate<PlanetData>();
        state.RequireForUpdate<Player>();

        //_playerQuery = state.GetEntityQuery(ComponentType.ReadOnly<Player>());
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton<GameState>(out var gameState) || gameState.State != EGameState.Running)
            return;

        //if (_playerQuery.IsEmpty)
        //    return;

        ref var spawnerState = ref SystemAPI.GetSingletonRW<SpawnerState>().ValueRW;
        DynamicBuffer<Wave> waves = SystemAPI.GetSingletonBuffer<Wave>(true);
        DynamicBuffer<SpawnGroup> groups = SystemAPI.GetSingletonBuffer<SpawnGroup>(true);
        DynamicBuffer<SpawnGroupRuntime> groupRuntimes = SystemAPI.GetSingletonBuffer<SpawnGroupRuntime>(false);
        SpawnerSettings settings = SystemAPI.GetSingleton<SpawnerSettings>();


        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        // Handle kills
        ProcessKills(ref ecb, ref state, ref spawnerState);
        // Handle timer and wave progression
        ManageWaveProgression(ref state, ref ecb, ref spawnerState, waves, groups, groupRuntimes);
        // Handle Spawning
        ManageSpawning(ref ecb, ref state, ref spawnerState, waves, groups, groupRuntimes, settings.MaxEnemies);
    }

    private void ProcessKills(ref EntityCommandBuffer ecb, ref SystemState state, ref SpawnerState spawnerState)
    {
        foreach (var (evt, entity) in SystemAPI.Query<RefRO<EnemyKilledEvent>>().WithEntityAccess())
        {
            if (evt.ValueRO.WaveIndex == spawnerState.CurrentWaveIndex)
                spawnerState.EnemiesKilledInWave++;

            spawnerState.ActiveEnemyCount--;
            if (spawnerState.ActiveEnemyCount < 0)
                spawnerState.ActiveEnemyCount = 0;

            ecb.DestroyEntity(entity);
        }
    }

    private void ManageWaveProgression(ref SystemState systemState, ref EntityCommandBuffer ecb,
        ref SpawnerState spawnerState, DynamicBuffer<Wave> waves, DynamicBuffer<SpawnGroup> groups,
        DynamicBuffer<SpawnGroupRuntime> groupRuntimes)
    {
        if (waves.Length == 0)
            return; // dirty condition, but never mind

        if (spawnerState.CurrentWaveIndex == -1)
        {
            StartWave(ref spawnerState, waves, groups, groupRuntimes, 0);
            return;
        }

        if (spawnerState.CurrentWaveIndex >= waves.Length)
            return;

        Wave currentWave = waves[spawnerState.CurrentWaveIndex];
        spawnerState.WaveTimer -= SystemAPI.Time.DeltaTime;

        // Next wave conditions
        bool timeOut = spawnerState.WaveTimer <= 0;
        bool killPercentReached = false;

        if (currentWave.TotalEnemyCount > 0)
        {
            float killRatio = (float)spawnerState.EnemiesKilledInWave / currentWave.TotalEnemyCount;
            killPercentReached = killRatio >= currentWave.KillPercentage;
        }

        if (timeOut || killPercentReached)
        {
            // Exists next wave?
            if (spawnerState.CurrentWaveIndex + 1 < waves.Length)
            {
                StartWave(ref spawnerState, waves, groups, groupRuntimes, spawnerState.CurrentWaveIndex + 1);
            }
        }
    }

    /// <summary>
    /// Advances every group of the active wave independently and in parallel. Each group "popcorns"
    /// its enemies, releasing one every <see cref="SpawnGroup.SpawnDelay"/> seconds (a delay of 0 or
    /// less dumps the whole group at once). A single per-frame budget (frame-rate + hard enemy cap) is
    /// shared across all groups; spawns the budget cannot fit are deferred, never dropped.
    /// </summary>
    private void ManageSpawning(ref EntityCommandBuffer ecb, ref SystemState systemState, ref SpawnerState spawnerState,
        DynamicBuffer<Wave> waves, DynamicBuffer<SpawnGroup> groups, DynamicBuffer<SpawnGroupRuntime> groupRuntimes,
        int maxEnemies)
    {
        if (spawnerState.CurrentWaveIndex < 0)
            return;

        // Global per-frame safety budget shared by every group (frame-rate spike guard + hard enemy cap).
        int frameBudget = math.min(MAX_SPAWNS_PER_FRAME, maxEnemies - spawnerState.ActiveEnemyCount);
        if (frameBudget <= 0)
            return;

        var currentWave = waves[spawnerState.CurrentWaveIndex];
        int endGroupIndex = currentWave.GroupStartIndex + currentWave.GroupCount;

        float dt = SystemAPI.Time.DeltaTime;
        // Distinct ECB sort-key range per group scheduled this frame so their commands never collide.
        int sortKeyBase = 0;

        for (int gi = currentWave.GroupStartIndex; gi < endGroupIndex && frameBudget > 0; gi++)
        {
            var runtime = groupRuntimes[gi];
            if (runtime.Remaining <= 0)
                continue; // group finished (or not part of this wave)

            var group = groups[gi];

            // Cap by both the group's remaining count and the shared frame budget.
            int allowed = math.min(frameBudget, runtime.Remaining);
            int countToSpawn;

            if (group.SpawnDelay <= 0f)
            {
                // No inter-spawn delay: release as many as the budget allows this frame.
                countToSpawn = allowed;
            }
            else
            {
                // Popcorn: tick the timer and release one enemy per elapsed delay (catch-up included).
                // If the budget caps us below what time owes, the timer stays negative and carries the
                // backlog to later frames.
                runtime.SpawnTimer -= dt;
                countToSpawn = 0;
                while (runtime.SpawnTimer <= 0f && countToSpawn < allowed)
                {
                    countToSpawn++;
                    runtime.SpawnTimer += group.SpawnDelay;
                }
            }

            if (countToSpawn > 0)
            {
                // Enemies already released in this group -> preserves the geometric layout across batches.
                int startIndex = group.Amount - runtime.Remaining;
                ScheduleSpawnJob(ref ecb, ref systemState, group, startIndex, countToSpawn,
                    spawnerState.CurrentWaveIndex, sortKeyBase);

                runtime.Remaining -= countToSpawn;
                spawnerState.ActiveEnemyCount += countToSpawn;
                spawnerState.TotalEnemiesSpawnedInWave += countToSpawn;

                frameBudget -= countToSpawn;
                sortKeyBase += countToSpawn;
            }

            groupRuntimes[gi] = runtime;
        }
    }

    private void StartWave(ref SpawnerState spawnerState, DynamicBuffer<Wave> waves, DynamicBuffer<SpawnGroup> groups,
        DynamicBuffer<SpawnGroupRuntime> groupRuntimes, int index)
    {
        Wave wave = waves[index];
        spawnerState.CurrentWaveIndex = index;
        spawnerState.WaveTimer = wave.Duration;

        // Reset counters
        spawnerState.EnemiesKilledInWave = 0;
        spawnerState.TotalEnemiesSpawnedInWave = 0;
        spawnerState.TotalEnemiesToSpawnInWave = wave.TotalEnemyCount;

        // Activate every group of this wave at once: they all start popcorning in parallel, with the
        // first enemy of each popping on the next spawn tick (SpawnTimer = 0).
        int endGroupIndex = wave.GroupStartIndex + wave.GroupCount;
        for (int gi = wave.GroupStartIndex; gi < endGroupIndex; gi++)
        {
            groupRuntimes[gi] = new SpawnGroupRuntime
            {
                Remaining = groups[gi].Amount,
                SpawnTimer = 0f
            };
        }
    }

    private void ScheduleSpawnJob(ref EntityCommandBuffer ecb, ref SystemState state, SpawnGroup group, int startIndex,
        int count, int waveIndex, int sortKeyBase)
    {
        var physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>();

        var planetEntity = SystemAPI.GetSingletonEntity<PlanetData>();
        var planetData = SystemAPI.GetComponentRO<PlanetData>(planetEntity).ValueRO;
        var planetTransform = SystemAPI.GetComponentRO<LocalTransform>(planetEntity).ValueRO;

        var playerEntity = SystemAPI.GetSingletonEntity<Player>();

        if (playerEntity == Entity.Null)
            return;

        var playerTransform = SystemAPI.GetComponentRO<LocalTransform>(playerEntity).ValueRO;

        var spawnJob = new SpawnJob
        {
            ECB = ecb.AsParallelWriter(),

            CollisionWorld = physicsWorld.CollisionWorld,

            PlayerTransform = playerTransform,
            Prefab = group.Prefab,

            TotalAmount = group.Amount,
            StartIndex = startIndex,
            Mode = group.Mode,
            WaveIndex = waveIndex,
            SortKeyBase = sortKeyBase,
            // Scale = group.Scale, // issue ? @todo

            PlanetCenter = planetTransform.Position,
            PlanetRadius = planetData.Radius,

            SpawnOrigin = group.Position,
            MinRange = group.MinRange,
            MaxRange = group.MaxRange,

            // Offset by sortKeyBase so groups spawning on the same frame don't share random seeds.
            Seed = (uint)(SystemAPI.Time.ElapsedTime * 1000) + 1 + (uint)sortKeyBase
        };

        state.Dependency = spawnJob.ScheduleParallel(count, 64, state.Dependency);
    }

    [BurstCompile]
    private struct SpawnJob : IJobFor
    {
        public EntityCommandBuffer.ParallelWriter ECB;

        [ReadOnly] public CollisionWorld CollisionWorld;

        [ReadOnly] public LocalTransform PlayerTransform;
        [ReadOnly] public Entity Prefab;

        // Spawn Configuration
        public int TotalAmount;
        public int StartIndex;
        public SpawnMode Mode;
        public int WaveIndex;
        public float Scale;

        /// <summary> Base offset added to every ECB sort key so concurrent group jobs stay disjoint. </summary>
        public int SortKeyBase;

        // Planet Data
        public float3 PlanetCenter;
        public float PlanetRadius;

        // Spawn Parameters
        public float3 SpawnOrigin;
        public float MinRange;
        public float MaxRange;

        // Random
        public uint Seed;

        public void Execute(int index)
        {
            int globalIndex = StartIndex + index;
            var rand = Random.CreateFromIndex(Seed + (uint)globalIndex);

            float3 spawnPosition = float3.zero;
            float3 surfaceNormal = float3.zero;
            bool positionFound = false;

            var groundFilter = new CollisionFilter
            {
                BelongsTo = CollisionLayers.Raycast,
                CollidesWith = CollisionLayers.Landscape
            };

            // Calculate spawn postion based on spawning mode
            switch (Mode)
            {
                // case SpawnMode.RandomInPlanet:
                //     float3 randomDir = rand.NextFloat3Direction();
                //     float3 roughPos = PlanetCenter + randomDir * PlanetRadius;
                //
                //     if (PlanetUtils.SnapToSurfaceRaycast(ref CollisionWorld, roughPos, PlanetCenter, groundFilter, 150f,
                //             out var hit))
                //     {
                //         spawnPosition = hit.Position;
                //         surfaceNormal = hit.SurfaceNormal;
                //         positionFound = true;
                //     }
                //
                //     break;

                case SpawnMode.RandomInPlanet:
                    float goldenAngleSphere = 2.39996323f; // PI * (3 - sqrt(5))

                    float maxAmount = math.max(1f, (float)TotalAmount);
                    float z = 1f - (2f * globalIndex + 1f) / maxAmount;

                    float radiusAtZ = math.sqrt(1f - z * z);

                    float thetaSphere = goldenAngleSphere * globalIndex;

                    float x = radiusAtZ * math.cos(thetaSphere);
                    float y = radiusAtZ * math.sin(thetaSphere);

                    float3 sphereDirection = new float3(x, y, z);
                    float3 roughPosPlanet = PlanetCenter + (sphereDirection * PlanetRadius);

                    if (PlanetUtils.SnapToSurfaceRaycast(ref CollisionWorld, roughPosPlanet, PlanetCenter, groundFilter,
                            150f, out var hitPlanet))
                    {
                        spawnPosition = hitPlanet.Position;
                        surfaceNormal = hitPlanet.SurfaceNormal;
                        positionFound = true;
                    }

                    break;

                // case SpawnMode.Zone:
                //     float zoneRadius = math.max(5f, TotalAmount * 0.5f);
                //     positionFound = PlanetUtils.GetRandomPointOnSurface(
                //         ref CollisionWorld, ref rand, SpawnOrigin, PlanetCenter, zoneRadius, ref groundFilter,
                //         out spawnPosition, out surfaceNormal);
                //     break;

                case SpawnMode.Zone:
                    float goldenAngle = 2.39996323f;

                    float c = MaxRange / math.sqrt(TotalAmount);

                    float angle = globalIndex * goldenAngle;
                    float spiralRadius = c * math.sqrt(globalIndex);

                    spiralRadius += MinRange;

                    float2 perfectCircle = new float2(math.cos(angle), math.sin(angle)) * spiralRadius;

                    float3 up = math.normalize(SpawnOrigin - PlanetCenter);
                    float3 tangent = math.cross(up, new float3(0, 1, 0));
                    if (math.lengthsq(tangent) < 0.001f)
                        tangent = math.cross(up, new float3(1, 0, 0));

                    quaternion alignmentRot = quaternion.LookRotationSafe(tangent, up);
                    float3 localOffset = new float3(perfectCircle.x, 0f, perfectCircle.y);
                    float3 worldOffset = math.rotate(alignmentRot, localOffset);
                    float3 p = SpawnOrigin + worldOffset;

                    if (PlanetUtils.SnapToSurfaceRaycast(ref CollisionWorld, p, PlanetCenter, groundFilter, 100f,
                            out var h))
                    {
                        spawnPosition = h.Position;
                        surfaceNormal = h.SurfaceNormal;
                        positionFound = true;
                    }

                    break;

                case SpawnMode.PlayerOpposite:
                    // Opposite point from player
                    float3 playerPosition = PlayerTransform.Position;
                    float3 dirToOrigin = math.normalize(playerPosition - PlanetCenter);
                    float3 opositePoint = PlanetCenter - (dirToOrigin * PlanetRadius * 1f);

                    // Avoid stacking
                    float oppositePositionRadius = math.max(15f, TotalAmount * 0.5f);

                    positionFound = PlanetUtils.GetRandomPointOnSurface(
                        ref CollisionWorld, ref rand, opositePoint, PlanetCenter, oppositePositionRadius,
                        ref groundFilter,
                        out spawnPosition, out surfaceNormal);
                    break;

                // case SpawnMode.AroundPlayer:
                //     positionFound = PlanetUtils.GetRandomPointOnSurface(
                //         ref CollisionWorld, ref rand, SpawnOrigin, PlanetCenter, MinRange, MaxRange, ref groundFilter,
                //         out spawnPosition, out surfaceNormal);
                //     break;

                case SpawnMode.AroundPlayer:
                    float goldenAngleAround = 2.39996323f;
                    float angleAround = globalIndex * goldenAngleAround;

                    float fraction = (float)globalIndex / TotalAmount;

                    float radiusAround = math.lerp(MinRange, MaxRange, math.sqrt(fraction));

                    float2 perfectCircleAround =
                        new float2(math.cos(angleAround), math.sin(angleAround)) * radiusAround;

                    // AroundPlayer centers the spawn ring on the player (not the group origin).
                    float3 centerAround = PlayerTransform.Position;
                    float3 upAround = math.normalize(centerAround - PlanetCenter);
                    float3 tangentAround = math.cross(upAround, new float3(0, 1, 0));
                    if (math.lengthsq(tangentAround) < 0.001f)
                        tangentAround = math.cross(upAround, new float3(1, 0, 0));

                    quaternion alignmentRotAround = quaternion.LookRotationSafe(tangentAround, upAround);
                    float3 localOffsetAround = new float3(perfectCircleAround.x, 0f, perfectCircleAround.y);
                    float3 worldOffsetAround = math.rotate(alignmentRotAround, localOffsetAround);
                    float3 roughPosAround = centerAround + worldOffsetAround;

                    if (PlanetUtils.SnapToSurfaceRaycast(ref CollisionWorld, roughPosAround, PlanetCenter, groundFilter,
                            100f, out var hitAround))
                    {
                        spawnPosition = hitAround.Position;
                        surfaceNormal = hitAround.SurfaceNormal;
                        positionFound = true;
                    }

                    break;
            }

            if (!positionFound)
                return;

            // Instantiate the enemy from the prefab
            int sortKey = SortKeyBase + index;
            Entity entity = ECB.Instantiate(sortKey, Prefab);

            // Entity orientation
            float3 randomTangent = rand.NextFloat3Direction();
            float3 tangentDirection =
                math.normalize(randomTangent - math.dot(randomTangent, surfaceNormal) * surfaceNormal);

            float3 spawnOffset = rand.NextFloat(0f, 8f); // Avoid overlap
            float3 finalPosition =
                spawnPosition + (tangentDirection * spawnOffset) + (surfaceNormal * 0.5f);

            // Set Transform
            float spawnScale = Scale > 0f ? Scale : 1f;
            ECB.SetComponent(sortKey, entity, new LocalTransform
            {
                Position = finalPosition,
                Scale = spawnScale,
                Rotation = quaternion.LookRotationSafe(tangentDirection, surfaceNormal)
            });

            // Set Movement Target
            // ECB.SetComponent(sortKey, entity, new FollowTargetMovement
            ECB.SetComponent(sortKey, entity, new FlowFieldFollowerMovement());

            // Set Wave Index
            // todo @hyverno passing Enemy in lookup
            ECB.SetComponent(sortKey, entity, new Enemy { WaveIndex = WaveIndex });
        }
    }
}