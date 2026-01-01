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
    // Maximum number of entities to spawn per frame to maintain performance
    private const int MaxSpawnsPerFrame = 100;

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

        ref var spawnerState = ref SystemAPI.GetSingletonRW<SpawnerState>().ValueRW;
        var waveBuffer = SystemAPI.GetSingletonBuffer<WaveElement>(true);

        // If we have pending spawns from a previous frame, continue processing them
        if (spawnerState.PendingSpawnCount > 0)
        {
            ProcessPendingSpawns(ref state, ref spawnerState, waveBuffer);
            return;
        }

        // Manage Wave Timer
        spawnerState.WaveTimer -= SystemAPI.Time.DeltaTime;
        
        if (spawnerState.WaveTimer > 0)
            return;

        // Reset timer for next wave
        var settings = SystemAPI.GetSingleton<SpawnerSettings>();
        spawnerState.WaveTimer = settings.TimeBetweenWaves;

        // Start processing the new wave
        // We iterate through all elements of the current wave to see if we need to start spawning
        // Note: This logic assumes we process all elements of a wave index sequentially or together.
        // If a wave has multiple elements, we might need to handle them one by one or all together.
        // For simplicity and to support the request, let's check if we need to start a multi-frame spawn sequence.
        
        // Check if there are any elements for the current wave
        bool waveHasElements = false;
        for (int i = 0; i < waveBuffer.Length; i++)
        {
            if (waveBuffer[i].WaveIndex == spawnerState.CurrentWaveIndex)
            {
                waveHasElements = true;
                break;
            }
        }

        if (waveHasElements)
        {
            // Initialize multi-frame spawning state
            // We will process wave elements one by one or in batches.
            // Let's start with the first element of the wave.
            spawnerState.CurrentWaveElementIndex = 0; // We'll search for the first matching element index
            
            // Find the first element index for this wave
            int firstElementIndex = -1;
            for(int i=0; i<waveBuffer.Length; i++) {
                if(waveBuffer[i].WaveIndex == spawnerState.CurrentWaveIndex) {
                    firstElementIndex = i;
                    break;
                }
            }
            
            if (firstElementIndex != -1)
            {
                spawnerState.CurrentWaveElementIndex = firstElementIndex;
                var element = waveBuffer[firstElementIndex];
                spawnerState.PendingSpawnCount = element.Amount;
                spawnerState.SpawnsProcessed = 0;
                
                // Immediately process some spawns this frame
                ProcessPendingSpawns(ref state, ref spawnerState, waveBuffer);
            }
            else
            {
                // Should not happen if waveHasElements is true, but just in case
                spawnerState.CurrentWaveIndex++;
            }
        }
        else
        {
            // No elements for this wave index, maybe end of game or empty wave?
            // Just move to next index? Or maybe loop? 
            // For now, let's just increment to avoid getting stuck if it's an empty wave slot
             spawnerState.CurrentWaveIndex++;
        }
    }

    private void ProcessPendingSpawns(ref SystemState state, ref SpawnerState spawnerState, DynamicBuffer<WaveElement> waveBuffer)
    {
        // Validate index
        if (spawnerState.CurrentWaveElementIndex < 0 || spawnerState.CurrentWaveElementIndex >= waveBuffer.Length)
        {
            // Something went wrong or we finished all elements?
            spawnerState.PendingSpawnCount = 0;
            return;
        }

        var waveElement = waveBuffer[spawnerState.CurrentWaveElementIndex];
        
        // Double check we are on the right wave (should be guaranteed by logic)
        if (waveElement.WaveIndex != spawnerState.CurrentWaveIndex)
        {
            // We somehow drifted? Stop.
            spawnerState.PendingSpawnCount = 0;
            return;
        }

        int amountToSpawn = math.min(spawnerState.PendingSpawnCount, MaxSpawnsPerFrame);
        
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
                spawnOrigin = float3.zero; 
                seedOffset = 1;
                break;
            case SpawnMode.AroundPlayer:
                spawnOrigin = playerTransform.Position;
                seedOffset = 3;
                break;
        }

        var spawnJob = new SpawnJob
        {
            ECB = ecb,
            PlayerEntity = playerEntity,
            TotalAmount = waveElement.Amount, // Total amount for the whole wave element, needed for circle calculations
            StartIndex = spawnerState.SpawnsProcessed, // Offset for this batch
            Prefab = waveElement.Prefab,
            PlanetCenter = planetCenter,
            PlanetRadius = planetRadius,
            SpawnOrigin = spawnOrigin,
            BaseSeed = (uint)(SystemAPI.Time.ElapsedTime * 1000) + (uint)spawnerState.CurrentWaveElementIndex * 7919 + seedOffset,
            DelayBetweenSpawns = waveElement.SpawnDelay,
            Mode = waveElement.Mode,
            MinRange = waveElement.MinSpawnRange,
            MaxRange = waveElement.MaxSpawnRange
        };

        state.Dependency = spawnJob.ScheduleParallel(amountToSpawn, 64, state.Dependency);

        // Update state
        spawnerState.PendingSpawnCount -= amountToSpawn;
        spawnerState.SpawnsProcessed += amountToSpawn;

        // If we finished this element, check if there are more elements for the SAME wave index
        if (spawnerState.PendingSpawnCount <= 0)
        {
            // Look for next element with same WaveIndex
            int nextElementIndex = -1;
            for (int i = spawnerState.CurrentWaveElementIndex + 1; i < waveBuffer.Length; i++)
            {
                if (waveBuffer[i].WaveIndex == spawnerState.CurrentWaveIndex)
                {
                    nextElementIndex = i;
                    break;
                }
            }

            if (nextElementIndex != -1)
            {
                // Found another element for this wave, setup for next frame (or next call)
                spawnerState.CurrentWaveElementIndex = nextElementIndex;
                spawnerState.PendingSpawnCount = waveBuffer[nextElementIndex].Amount;
                spawnerState.SpawnsProcessed = 0;
            }
            else
            {
                // No more elements for this wave, we are done with this wave
                spawnerState.CurrentWaveIndex++;
                spawnerState.PendingSpawnCount = 0;
                spawnerState.SpawnsProcessed = 0;
                spawnerState.CurrentWaveElementIndex = -1;
            }
        }
    }

    [BurstCompile]
    private struct SpawnJob : IJobFor
    {
        public EntityCommandBuffer.ParallelWriter ECB;
        public Entity PlayerEntity;
        public int TotalAmount;
        public int StartIndex; // The global index offset for this batch
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
            // Calculate the actual global index for this spawn
            int globalIndex = StartIndex + index;
            
            var rand = Random.CreateFromIndex(BaseSeed + (uint)globalIndex);
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

                // Calculate position on the circle using globalIndex
                float angle = (2 * math.PI * globalIndex) / TotalAmount;
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
            // Use index (local to this job batch) for sort key to allow parallel writing
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
                Timer = globalIndex * DelayBetweenSpawns
            });
            
            // Start disabled, enabled by another system after delay
            ECB.AddComponent<Disabled>(index, entity);
        }
    }
}
