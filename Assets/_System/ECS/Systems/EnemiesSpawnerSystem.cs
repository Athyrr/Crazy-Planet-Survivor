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

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
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
        SpawnerSettings settings = SystemAPI.GetSingleton<SpawnerSettings>();


        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        // Handle kills
        ProcessKills(ref ecb, ref state, ref spawnerState);
        // Handle timer and wave progression
        ManageWaveProgression(ref state, ref ecb, ref spawnerState, waves);
        // Handle Spawning
        ManageSpawning(ref ecb, ref state, ref spawnerState, groups, settings.MaxEnemies);
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

    private void ManageWaveProgression(ref SystemState systemState, ref EntityCommandBuffer ecb, ref SpawnerState spawnerState, DynamicBuffer<Wave> waves)
    {
        if (spawnerState.CurrentWaveIndex == -1)
        {
            StartWave(ref spawnerState, waves, 0);
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
                StartWave(ref spawnerState, waves, spawnerState.CurrentWaveIndex + 1);
            }
            else
            {
                //state.IsWaveActive = false;
                var endRunRequestEntity = ecb.CreateEntity();
                ecb.AddComponent(endRunRequestEntity, new EndRunRequest
                {
                    State = EEndRunState.Success
                });
            }
        }
    }

    private void ManageSpawning(ref EntityCommandBuffer ecb, ref SystemState systemState, ref SpawnerState spawnerState, DynamicBuffer<SpawnGroup> groups, int maxEnemies)
    {
        if (spawnerState.CurrentWaveIndex < 0)
            return;

        // Cancel spawning if max enemies count is reached
        if (spawnerState.ActiveEnemyCount >= maxEnemies)
            return;

        var waves = SystemAPI.GetSingletonBuffer<Wave>(true);
        var currentWave = waves[spawnerState.CurrentWaveIndex];
        int endGroupIndex = currentWave.GroupStartIndex + currentWave.GroupCount;

        // If -1 -> init new group 
        bool mustInitGroup = spawnerState.RemainingSpawnsInGroup == -1;
        if (mustInitGroup)
        {
            if (spawnerState.CurrentGroupIndex < endGroupIndex) // Still remaining groups in wave
            {
                spawnerState.RemainingSpawnsInGroup = groups[spawnerState.CurrentGroupIndex].Amount;
            }
            else
            {
                spawnerState.RemainingSpawnsInGroup = 0; // Notify no more enemies in group
                return;
            }
        }

        // Remains enemies to spawn
        if (spawnerState.RemainingSpawnsInGroup > 0)
        {
            var group = groups[spawnerState.CurrentGroupIndex];

            int canSpawnCount = math.min(MAX_SPAWNS_PER_FRAME, maxEnemies - spawnerState.ActiveEnemyCount);
            int countToSpawn = math.min(canSpawnCount, spawnerState.RemainingSpawnsInGroup);

            if (countToSpawn > 0)
            {
                // Spawn enemies
                ScheduleSpawnJob(ref ecb, ref systemState, group, countToSpawn, spawnerState.CurrentWaveIndex);

                // Update state
                spawnerState.RemainingSpawnsInGroup -= countToSpawn;
                spawnerState.ActiveEnemyCount += countToSpawn;
                spawnerState.TotalEnemiesSpawnedInWave += countToSpawn;
            }
        }
        else
        {
            // Group spawning ended
            spawnerState.CurrentGroupIndex++;
            if (spawnerState.CurrentGroupIndex < endGroupIndex)
            {
                spawnerState.RemainingSpawnsInGroup = groups[spawnerState.CurrentGroupIndex].Amount;
            }
            else
            {
                // All groups of this wave have been spawned
                // Wait for next wave spawn condtion (ManageWaveProgression)
            }
        }
    }

    private void StartWave(ref SpawnerState spawnerState, DynamicBuffer<Wave> waves, int index)
    {
        Wave wave = waves[index];
        spawnerState.CurrentWaveIndex = index;
        spawnerState.WaveTimer = wave.Duration;

        // Reset counters
        spawnerState.EnemiesKilledInWave = 0;
        spawnerState.TotalEnemiesSpawnedInWave = 0;
        spawnerState.TotalEnemiesToSpawnInWave = wave.TotalEnemyCount;

        spawnerState.CurrentGroupIndex = wave.GroupStartIndex;

        bool hasGroups = wave.GroupCount > 0;
        if (hasGroups)
        {
            spawnerState.RemainingSpawnsInGroup = -1; // -1 = Has to be filled by ManageSpawning
        }
        else
        {
            spawnerState.RemainingSpawnsInGroup = 0; // 0 = No more enemies to spawn, process the next group
        }
    }

    private void ScheduleSpawnJob(ref EntityCommandBuffer ecb, ref SystemState state, SpawnGroup group, int count, int waveIndex)
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

            PlayerEntity = playerEntity,
            PlayerTransform = playerTransform,
            Prefab = group.Prefab,

            TotalAmount = group.Amount,
            StartIndex = group.Amount - count,
            Mode = group.Mode,
            WaveIndex = waveIndex,

            PlanetCenter = planetTransform.Position,
            PlanetRadius = planetData.Radius,

            SpawnOrigin = group.Position,
            MinRange = group.MinRange,
            MaxRange = group.MaxRange,

            Seed = (uint)(SystemAPI.Time.ElapsedTime * 1000) + 1
        };

        state.Dependency = spawnJob.ScheduleParallel(count, 64, state.Dependency);
    }

    [BurstCompile]
    private struct SpawnJob : IJobFor
    {
        public EntityCommandBuffer.ParallelWriter ECB;

        [ReadOnly] public CollisionWorld CollisionWorld;

        [ReadOnly] public Entity PlayerEntity;
        [ReadOnly] public LocalTransform PlayerTransform;
        [ReadOnly] public Entity Prefab;

        // Spawn Configuration
        public int TotalAmount;
        public int StartIndex;
        public SpawnMode Mode;
        public int WaveIndex;

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
                case SpawnMode.RandomInPlanet:
                    float3 randomDir = rand.NextFloat3Direction();
                    float3 roughPos = PlanetCenter + randomDir * PlanetRadius;

                    if (PlanetUtils.SnapToSurfaceRaycast(ref CollisionWorld, roughPos, PlanetCenter, groundFilter, 150f, out var hit))
                    {
                        spawnPosition = hit.Position;
                        surfaceNormal = hit.SurfaceNormal;
                        positionFound = true;
                    }
                    break;

                case SpawnMode.Zone:
                    float zoneRadius = math.max(5f, TotalAmount * 0.5f);
                    positionFound = PlanetUtils.GetRandomPointOnSurface(
                        ref CollisionWorld, ref rand, SpawnOrigin, PlanetCenter, zoneRadius, ref groundFilter,
                        out spawnPosition, out surfaceNormal);
                    break;

                case SpawnMode.PlayerOpposite:
                    // Opposite point from player
                    float3 playerPosition = PlayerTransform.Position;
                    float3 dirToOrigin = math.normalize(playerPosition - PlanetCenter);
                    float3 opositePoint = PlanetCenter - (dirToOrigin * PlanetRadius * 1f);

                    // Avoid stacking
                    float oppositePositionRadius = math.max(15f, TotalAmount * 0.5f);

                    positionFound = PlanetUtils.GetRandomPointOnSurface(
                        ref CollisionWorld, ref rand, opositePoint, PlanetCenter, oppositePositionRadius, ref groundFilter,
                        out spawnPosition, out surfaceNormal);
                    break;

                case SpawnMode.AroundPlayer:
                    positionFound = PlanetUtils.GetRandomPointOnSurface(
                        ref CollisionWorld, ref rand, SpawnOrigin, PlanetCenter, MinRange, MaxRange, ref groundFilter,
                        out spawnPosition, out surfaceNormal);
                    break;
            }

            if (!positionFound)
                return;

            // Instantiate the enemy from the prefab
            Entity entity = ECB.Instantiate(index, Prefab);

            // Entity orientation
            float3 randomTangent = rand.NextFloat3Direction();
            float3 tangentDirection = math.normalize(randomTangent - math.dot(randomTangent, surfaceNormal) * surfaceNormal);

            float3 finalPosition = spawnPosition + (surfaceNormal * 0.5f);

            // Set Transform 
            ECB.SetComponent(index, entity, new LocalTransform
            {
                Position = finalPosition,
                Scale = 1f,
                Rotation = quaternion.LookRotationSafe(tangentDirection, surfaceNormal)
            });

            // Set Movement Target
            ECB.SetComponent(index, entity, new FollowTargetMovement
            {
                Target = PlayerEntity
            });

            // Set Wave Index
            ECB.SetComponent(index, entity, new Enemy { WaveIndex = WaveIndex });
        }
    }
}
