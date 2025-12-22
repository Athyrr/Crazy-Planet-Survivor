using Unity.Jobs;
using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;

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
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton<GameState>(out var gameState))
            return;

        if (gameState.State != EGameState.Running)
            return;

        ref var spawnerState = ref SystemAPI.GetSingletonRW<SpawnerState>().ValueRW;
        spawnerState.WaveTimer -= SystemAPI.Time.DeltaTime;
        if (spawnerState.WaveTimer > 0)
            return;

        var settings = SystemAPI.GetSingleton<SpawnerSettings>();
        spawnerState.WaveTimer = settings.TimeBetweenWaves;

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

        for (int i = 0; i < waveBuffer.Length; i++)
        {
            var waveElement = waveBuffer[i];

            if (waveElement.WaveIndex != spawnerState.CurrentWaveIndex)
                continue;

            if (waveElement.Mode == SpawnMode.Single)
            {
                var singleJob = new SpawnJob
                {
                    ECB = ecb,
                    PlayerEntity = playerEntity,
                    TotalAmount = waveElement.Amount,
                    Prefab = waveElement.Prefab,
                    PlanetCenter = planetCenter,
                    PlanetRadius = planetRadius,
                    SpawnOrigin = waveElement.SpawnPosition,
                    BaseSeed = (uint)(SystemAPI.Time.ElapsedTime * 1000) + (uint)i * 7919,
                    DelayBetweenSpawns = waveElement.SpawnDelay,
                    Mode = waveElement.Mode
                };
                combinedHandle = singleJob.ScheduleParallel(waveElement.Amount, 64, combinedHandle);
            }
            else if (waveElement.Mode == SpawnMode.Opposite)
            {
                float3 dirToPlayer = math.normalize(playerTransform.Position - planetCenter);
                if (math.lengthsq(dirToPlayer) < 0.001f) dirToPlayer = new float3(0, 1, 0);

                float3 antipodalPos = planetCenter - dirToPlayer * planetRadius;

                var oppositeJob = new SpawnJob
                {
                    ECB = ecb,
                    PlayerEntity = playerEntity,
                    TotalAmount = waveElement.Amount,
                    Prefab = waveElement.Prefab,
                    PlanetCenter = planetCenter,
                    PlanetRadius = planetRadius,
                    SpawnOrigin = antipodalPos,
                    BaseSeed = (uint)(SystemAPI.Time.ElapsedTime * 1000) + (uint)i * 7919 + 2,
                    DelayBetweenSpawns = waveElement.SpawnDelay,
                    Mode = waveElement.Mode
                };
                combinedHandle = oppositeJob.ScheduleParallel(waveElement.Amount, 64, combinedHandle);
            }
            else if (waveElement.Mode == SpawnMode.EntirePlanet)
            {
                var spawnerJob = new SpawnJob
                {
                    ECB = ecb,
                    PlayerEntity = playerEntity,
                    TotalAmount = waveElement.Amount,
                    Prefab = waveElement.Prefab,
                    PlanetCenter = planetCenter,
                    PlanetRadius = planetData.Radius,
                    SpawnOrigin = float3.zero,
                    BaseSeed = (uint)(SystemAPI.Time.ElapsedTime * 1000) + (uint)i * 7919 + 1,
                    DelayBetweenSpawns = waveElement.SpawnDelay,
                    Mode = waveElement.Mode
                };
                combinedHandle = spawnerJob.ScheduleParallel(waveElement.Amount, 64, combinedHandle);
            }
            else if (waveElement.Mode == SpawnMode.AroundPlayer)
            {
                var aroundJob = new SpawnJob
                {
                    ECB = ecb,
                    PlayerEntity = playerEntity,
                    TotalAmount = waveElement.Amount,
                    Prefab = waveElement.Prefab,
                    PlanetCenter = planetCenter,
                    PlanetRadius = planetRadius,
                    SpawnOrigin = playerTransform.Position,
                    BaseSeed = (uint)(SystemAPI.Time.ElapsedTime * 1000) + (uint)i * 7919 + 3,
                    DelayBetweenSpawns = waveElement.SpawnDelay,
                    Mode = waveElement.Mode,
                    MinRange = waveElement.MinSpawnRange,
                    MaxRange = waveElement.MaxSpawnRange
                };
                combinedHandle = aroundJob.ScheduleParallel(waveElement.Amount, 64, combinedHandle);
            }
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
                    // Calculate basis for the circle on the sphere surface
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
                    
                    // Random direction around player
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

                // Random projected direction
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



                ECB.AddComponent(index, entity, new SpawnDelay
                {
                    Timer = index * DelayBetweenSpawns
                });
                ECB.AddComponent<Disabled>(index, entity);
        }
    }
}
