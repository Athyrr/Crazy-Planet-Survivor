using Unity.Jobs;
using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;

/// <summary>
/// System responsible for spawning enemies based on wave configuration.
/// It manages the wave timer and schedules jobs to instantiate enemies.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[BurstCompile]
public partial struct EnemiesSpawnerSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<SpawnerSettings>();
        state.RequireForUpdate<PlanetData>();
        state.RequireForUpdate<Player>();
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // Ensure the game is running
        if (!SystemAPI.TryGetSingleton<GameState>(out var gameState) || gameState.State != EGameState.Running)
            return;

        // Manage Wave Timer
        ref var spawnerState = ref SystemAPI.GetSingletonRW<SpawnerState>().ValueRW;
        spawnerState.WaveTimer -= SystemAPI.Time.DeltaTime;
        
        if (spawnerState.WaveTimer > 0)
            return;

        // Reset timer for next wave
        var settings = SystemAPI.GetSingleton<SpawnerSettings>();
        spawnerState.WaveTimer = settings.TimeBetweenWaves;

        // Prepare for spawning
        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter();

        Entity planetEntity = SystemAPI.GetSingletonEntity<PlanetData>();
        Entity playerEntity = SystemAPI.GetSingletonEntity<Player>();
        LocalTransform playerTransform = SystemAPI.GetComponentRO<LocalTransform>(playerEntity).ValueRO;

        PlanetData planetData = SystemAPI.GetComponentRO<PlanetData>(planetEntity).ValueRO;
        float planetRadius = planetData.Radius;
        LocalTransform planetTransform = SystemAPI.GetComponentRO<LocalTransform>(planetEntity).ValueRO;
        float3 planetCenter = planetTransform.Position;

        var waveBuffer = SystemAPI.GetSingletonBuffer<WaveElement>(true);
        JobHandle combinedHandle = state.Dependency;

        int enemiesSpawnedThisFrame = 0;

        // Iterate through all wave elements to find those matching the current wave index
        for (int i = 0; i < waveBuffer.Length; i++)
        {
            var waveElement = waveBuffer[i];

            if (waveElement.WaveIndex != spawnerState.CurrentWaveIndex)
                continue;

            enemiesSpawnedThisFrame += waveElement.Amount;

            // Determine spawn parameters based on mode
            float3 spawnOrigin = float3.zero;
            uint seedOffset = 0;

            switch (waveElement.Mode)
            {
                case SpawnMode.Single:
                    spawnOrigin = waveElement.SpawnPosition;
                    seedOffset = 0;
                    break;
                case SpawnMode.Opposite:
                    float3 dirToPlayer = math.normalize(playerTransform.Position - planetCenter);
                    if (math.lengthsq(dirToPlayer) < 0.001f) dirToPlayer = new float3(0, 1, 0);
                    spawnOrigin = planetCenter - dirToPlayer * planetRadius;
                    seedOffset = 2;
                    break;
                case SpawnMode.EntirePlanet:
                    spawnOrigin = float3.zero; // Not used for EntirePlanet, but need to be initialized 
                    seedOffset = 1;
                    break;
                case SpawnMode.AroundPlayer:
                    spawnOrigin = playerTransform.Position;
                    seedOffset = 3;
                    break;
            }

            // Used IJobFor instead of IJob and won 7 fps, on est trop chauds 
            var spawnJob = new SpawnJob
            {
                ECB = ecb,
                PlayerEntity = playerEntity,
                TotalAmount = waveElement.Amount,
                Prefab = waveElement.Prefab,
                PlanetCenter = planetCenter,
                PlanetRadius = planetRadius,
                SpawnOrigin = spawnOrigin,
                BaseSeed = (uint)(SystemAPI.Time.ElapsedTime * 1000) + (uint)i * 7919 + seedOffset,
                DelayBetweenSpawns = waveElement.SpawnDelay,
                Mode = waveElement.Mode,
                MinRange = waveElement.MinSpawnRange,
                MaxRange = waveElement.MaxSpawnRange
            };

            // Schedule the job in parallel. The '64' is the batch size, meaning each worker thread
            // will take chunks of 64 indices to process, reducing overhead.
            combinedHandle = spawnJob.ScheduleParallel(waveElement.Amount, 64, combinedHandle);
        }

        if (enemiesSpawnedThisFrame > 0 && SystemAPI.HasSingleton<GameStatistics>())
        {
            ref var stats = ref SystemAPI.GetSingletonRW<GameStatistics>().ValueRW;
            stats.EnemiesCreated += enemiesSpawnedThisFrame;
        }

        spawnerState.CurrentWaveIndex++;
        state.Dependency = combinedHandle;
    }

    [BurstCompile]
    private struct SpawnJob : IJobFor
    {
        public EntityCommandBuffer.ParallelWriter ECB;
        public Entity PlayerEntity;
        public int TotalAmount;
        public Entity Prefab;
        public float3 PlanetCenter;
        public float PlanetRadius;
        public float3 SpawnOrigin;
        public uint BaseSeed;
        public float DelayBetweenSpawns;
        public float MinRange;
        public float MaxRange;
        public SpawnMode Mode;

        public void Execute(int index)
        {
            var rand = Random.CreateFromIndex(BaseSeed + (uint)index);
            float3 spawnPosition = float3.zero;
            float3 normal = float3.zero;

            // Calculate Spawn Position based on Mode
            if (Mode == SpawnMode.EntirePlanet)
            {
                float3 randomDirection = rand.NextFloat3Direction();
                spawnPosition = PlanetCenter + randomDirection * PlanetRadius;
                normal = randomDirection;
            }
            else if (Mode == SpawnMode.Single)
            {
                spawnPosition = SpawnOrigin;
                normal = math.normalize(spawnPosition - PlanetCenter);
            }
            else if (Mode == SpawnMode.Opposite)
            {
                // Calculate basis for the circle on the sphere surface at the antipodal point
                float3 up = math.normalize(SpawnOrigin - PlanetCenter);
                
                // Arbitrary tangent
                float3 tangent = math.cross(up, new float3(0, 1, 0));
                if (math.lengthsq(tangent) < 0.001f)
                    tangent = math.cross(up, new float3(1, 0, 0));
                tangent = math.normalize(tangent);
                
                float3 bitangent = math.cross(up, tangent);

                // Calculate position on the circle
                float angle = (2 * math.PI * index) / TotalAmount;
                // Dynamic radius to prevent overlapping: circumference approx Amount * 1.5 units
                // Clamped to ensure it's not too small
                float radius = math.max(3f, TotalAmount * 0.25f); 
                
                float3 offset = (tangent * math.cos(angle) + bitangent * math.sin(angle)) * radius;
                
                // Project back onto sphere surface
                float3 rawPos = SpawnOrigin + offset;
                normal = math.normalize(rawPos - PlanetCenter);
                spawnPosition = PlanetCenter + normal * PlanetRadius;
            }
            else if (Mode == SpawnMode.AroundPlayer)
            {
                // SpawnOrigin is Player Position
                float3 playerUp = math.normalize(SpawnOrigin - PlanetCenter);
                
                // Random arc distance angle
                float minAngle = MinRange / PlanetRadius;
                float maxAngle = MaxRange / PlanetRadius;
                float randomAngle = rand.NextFloat(minAngle, maxAngle);
                
                // Random direction around player (azimuth)
                float randomAzimuth = rand.NextFloat(0, 2 * math.PI);
                
                // Construct rotation
                float3 tangent = math.cross(playerUp, new float3(0, 1, 0));
                if (math.lengthsq(tangent) < 0.001f) tangent = math.cross(playerUp, new float3(1, 0, 0));
                tangent = math.normalize(tangent);
                
                quaternion rotAzimuth = quaternion.AxisAngle(playerUp, randomAzimuth);
                float3 rotationAxis = math.rotate(rotAzimuth, tangent);
                
                quaternion rotArc = quaternion.AxisAngle(rotationAxis, randomAngle);
                float3 newNormal = math.rotate(rotArc, playerUp);
                
                spawnPosition = PlanetCenter + newNormal * PlanetRadius;
                normal = newNormal;
            }

            // Orientation: Look at random direction tangent to the surface
            float3 randomTangent = rand.NextFloat3Direction();
            float3 tangentDirection = randomTangent - math.dot(randomTangent, normal) * normal;
            tangentDirection = math.normalize(tangentDirection);

            // Instantiate and set components
            Entity entity = ECB.Instantiate(index, Prefab);

            ECB.SetComponent(index, entity, new LocalTransform
            {
                Position = spawnPosition,
                Scale = 1f,
                Rotation = quaternion.LookRotationSafe(tangentDirection, normal)
            });

            ECB.SetComponent(index, entity, new FollowTargetMovement
            {
                Target = PlayerEntity,
            });

            // Add spawn delay to stagger activation
            ECB.AddComponent(index, entity, new SpawnDelay
            {
                Timer = index * DelayBetweenSpawns
            });
            
            // Start disabled, enabled by another system after delay
            ECB.AddComponent<Disabled>(index, entity);
        }
    }
}
