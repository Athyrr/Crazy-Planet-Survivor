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

        PlanetData planetData = SystemAPI.GetComponentRO<PlanetData>(planetEntity).ValueRO;
        float planetRadius = planetData.Radius;
        LocalTransform planetTransform = SystemAPI.GetComponentRO<LocalTransform>(planetEntity).ValueRO;
        float3 planetCenter = planetTransform.Position;


        var waveBuffer = SystemAPI.GetSingletonBuffer<WaveElement>(true);
        JobHandle combinedHandle = state.Dependency;

        for (int i = 0; i < waveBuffer.Length; i++)
        {
            var waveElement = waveBuffer[i];

            if (waveElement.WaveIndex == spawnerState.CurrentWaveIndex)
            {
                var spawnerJob = new SpawnJob
                {
                    ECB = ecb,
                    PlayerEntity = playerEntity,
                    Amount = waveElement.Amount,
                    Prefab = waveElement.Prefab,
                    PlanetCenter = planetTransform.Position,
                    PlanetRadius = planetData.Radius,
                    Seed = (uint)(SystemAPI.Time.ElapsedTime * 1000) + (uint)i + 1
                };
                combinedHandle = spawnerJob.Schedule(combinedHandle);
            }
        }
        spawnerState.CurrentWaveIndex++;

        state.Dependency = combinedHandle;
    }

    [BurstCompile]
    private partial struct SpawnJob : IJob
    {
        public EntityCommandBuffer.ParallelWriter ECB;
        public Entity PlayerEntity;
        public int Amount;
        public Entity Prefab;
        public float3 PlanetCenter;
        public float PlanetRadius;
        public uint Seed;

        public void Execute()
        {
            var rand = Random.CreateFromIndex(Seed);

            for (int i = 0; i < Amount; i++)
            {
                float3 randomDirection = rand.NextFloat3Direction();
                float3 spawnPosition = PlanetCenter + randomDirection * PlanetRadius;

                // Planet normal at position
                float3 normal = randomDirection;

                // Random projected direction
                float3 randomTangent = rand.NextFloat3Direction();
                float3 tangentDirection = randomTangent - math.dot(randomTangent, normal) * normal;
                tangentDirection = math.normalize(tangentDirection);

                Entity entity = ECB.Instantiate(i, Prefab);

                ECB.SetComponent(i, entity, new LocalTransform
                {
                    Position = spawnPosition,
                    Scale = 1f,
                    Rotation = quaternion.LookRotationSafe(tangentDirection, normal)
                });

                ECB.SetComponent(i, entity, new FollowTargetMovement
                {
                    Target = PlayerEntity,
                });
            }
        }
    }
}
