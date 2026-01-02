using Unity.Jobs;
using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;

/// <summary>
/// Handles the logic for spawning enemies in waves on a spherical planet surface.
/// This system manages wave timing, processes pending spawn queues across multiple frames to prevent 
/// performance spikes, and calculates spawn positions based on various geometric modes.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[BurstCompile]
public partial struct EnemiesSpawnerSystem : ISystem
{
    /// <summary>
    /// Limits the number of entities instantiated in a single frame to maintain a stable frame rate.
    /// </summary>
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
        // Only process spawning logic while the game is in the 'Running' state
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

        // --- Wave Timer Management ---
        spawnerState.WaveTimer -= SystemAPI.Time.DeltaTime;
        
        if (spawnerState.WaveTimer > 0)
            return;

        var settings = SystemAPI.GetSingleton<SpawnerSettings>();
        spawnerState.WaveTimer = settings.TimeBetweenWaves;

        // --- New Wave Initialization ---
        
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
            // Find the first element index for this wave to begin the spawning sequence
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
            // If a wave index is empty, increment to prevent the spawner from stalling
             spawnerState.CurrentWaveIndex++;
        }
    }

    /// <summary>
    /// Calculates spawn parameters for the current batch and schedules the parallel instantiation job.
    /// </summary>
    /// <param name="state">The current system state.</param>
    /// <param name="spawnerState">The mutable state of the spawner singleton.</param>
    /// <param name="waveBuffer">The buffer containing wave configuration data.</param>
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
            spawnerState.PendingSpawnCount = 0;
            return;
        }

        // Clamp the amount to spawn this frame to the maximum allowed
        int amountToSpawn = math.min(spawnerState.PendingSpawnCount, MaxSpawnsPerFrame);
        
        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter();

        Entity planetEntity = SystemAPI.GetSingletonEntity<PlanetData>();
        Entity playerEntity = SystemAPI.GetSingletonEntity<Player>();
        LocalTransform playerTransform = SystemAPI.GetComponentRO<LocalTransform>(playerEntity).ValueRO;

        PlanetData planetData = SystemAPI.GetComponentRO<PlanetData>(planetEntity).ValueRO;
        float planetRadius = planetData.Radius;
        LocalTransform planetTransform = SystemAPI.GetComponentRO<LocalTransform>(planetEntity).ValueRO;
        float3 planetCenter = planetTransform.Position;

        // --- Spawn Origin Calculation ---
        float3 spawnOrigin = float3.zero;
        uint seedOffset = 0;

        switch (waveElement.Mode)
        {
            case SpawnMode.Single:
                spawnOrigin = waveElement.SpawnPosition;
                seedOffset = 0;
                break;
            case SpawnMode.Opposite:
                // Calculate the point on the planet surface directly opposite to the player
                float3 dirToPlayer = math.normalize(playerTransform.Position - planetCenter);
                if (math.lengthsq(dirToPlayer) < 0.001f) dirToPlayer = new float3(0, 1, 0);
                spawnOrigin = planetCenter - dirToPlayer * planetRadius;
                seedOffset = 2;
                break;
            case SpawnMode.EntirePlanet:
                // Origin is irrelevant for EntirePlanet mode as it uses random directions
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
            TotalAmount = waveElement.Amount,
            StartIndex = spawnerState.SpawnsProcessed,
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

        spawnerState.PendingSpawnCount -= amountToSpawn;
        spawnerState.SpawnsProcessed += amountToSpawn;

        // Check if we need to move to the next element in the current wave or increment the wave index
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

    /// <summary>
    /// Parallel job that calculates the specific world position and orientation for each enemy 
    /// based on the selected spawn mode and projects them onto the planet surface.
    /// </summary>
    [BurstCompile]
    private struct SpawnJob : IJobFor
    {
        public EntityCommandBuffer.ParallelWriter ECB;
        public Entity PlayerEntity;
        /// <summary> Total amount for the whole wave element, used for uniform distribution in circle modes. </summary>
        public int TotalAmount;
        /// <summary> The global index offset for this batch to ensure unique random seeds. </summary>
        public int StartIndex; 
        public Entity Prefab;
        public float3 PlanetCenter;
        public float PlanetRadius;
        public float3 SpawnOrigin;
        /// <summary> Base seed combined with global index for deterministic-ish randomness. </summary>
        public uint BaseSeed;
        public float DelayBetweenSpawns;
        public float MinRange;
        public float MaxRange;
        public SpawnMode Mode;

        /// <summary>
        /// Executes the spawning logic for a single entity in the batch.
        /// </summary>
        public void Execute(int index)
        {
            // Calculate the actual global index for this spawn
            int globalIndex = StartIndex + index;
            
            var rand = Random.CreateFromIndex(BaseSeed + (uint)globalIndex);
            float3 spawnPosition = float3.zero;
            float3 normal = float3.zero;

            // --- Position Calculation Logic ---
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
                // Calculate a coordinate basis (tangent/bitangent) at the antipodal point
                float3 up = math.normalize(SpawnOrigin - PlanetCenter);
                
                // Create an arbitrary tangent to start the basis
                float3 tangent = math.cross(up, new float3(0, 1, 0));
                if (math.lengthsq(tangent) < 0.001f)
                    tangent = math.cross(up, new float3(1, 0, 0));
                tangent = math.normalize(tangent);
                
                float3 bitangent = math.cross(up, tangent);

                // Distribute spawns in a circle around the antipodal point
                float angle = (2 * math.PI * globalIndex) / TotalAmount;
                // Scale radius based on amount to prevent immediate crowding
                float radius = math.max(3f, TotalAmount * 0.25f); 
                
                float3 offset = (tangent * math.cos(angle) + bitangent * math.sin(angle)) * radius;
                
                // Project back onto sphere surface
                float3 rawPos = SpawnOrigin + offset;
                normal = math.normalize(rawPos - PlanetCenter);
                spawnPosition = PlanetCenter + normal * PlanetRadius;
            }
            else if (Mode == SpawnMode.AroundPlayer)
            {
                float3 playerUp = math.normalize(SpawnOrigin - PlanetCenter);
                
                // Calculate a random distance from the player in radians (arc length / radius)
                float minAngle = MinRange / PlanetRadius;
                float maxAngle = MaxRange / PlanetRadius;
                float randomAngle = rand.NextFloat(minAngle, maxAngle);
                
                // Random azimuth (0 to 360 degrees)
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

            // --- Orientation Calculation ---
            // Ensure the enemy is looking in a direction tangent to the planet surface
            float3 randomTangent = rand.NextFloat3Direction();
            float3 tangentDirection = randomTangent - math.dot(randomTangent, normal) * normal;
            tangentDirection = math.normalize(tangentDirection);

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

            // Stagger the activation of enemies to create a "streaming" spawn effect
            ECB.AddComponent(index, entity, new SpawnDelay
            {
                Timer = globalIndex * DelayBetweenSpawns
            });
            
            // Enemies start disabled and are enabled by the SpawnDelaySystem
            ECB.AddComponent<Disabled>(index, entity);
        }
    }
}
