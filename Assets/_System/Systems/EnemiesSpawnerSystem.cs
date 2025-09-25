using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[BurstCompile]
public partial struct EnemiesSpawnerSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<SpawnConfig>();
        state.RequireForUpdate<PlanetData>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        SpawnConfig config = SystemAPI.GetSingleton<SpawnConfig>();
        Entity planetEntity = SystemAPI.GetSingletonEntity<PlanetData>();

        LocalTransform planetTransform = SystemAPI.GetComponentRO<LocalTransform>(planetEntity).ValueRO;
        PlanetData planetData = SystemAPI.GetComponentRO<PlanetData>(planetEntity).ValueRO;

        float3 planetCenter = planetTransform.Position;
        float planetRadius = planetData.Radius;

        Entity playerEntity = SystemAPI.GetSingletonEntity<Player>();

        //Random rand = new Random(45639);

        // Stop after the first tick
        state.Enabled = false;

        for (int i = 0; i < config.Amount; i++)
        {
            Random rand = new Random(45619 + (uint)i);

            float3 randomDirection = rand.NextFloat3Direction();
            float3 spawnPosition = planetCenter + randomDirection * planetRadius;

            // Planet normal at position
            float3 normal = randomDirection;

            // Random projected direction
            float3 randomTangent = rand.NextFloat3Direction();
            float3 tangentDirection = randomTangent - math.dot(randomTangent, normal) * normal;
            tangentDirection = math.normalize(tangentDirection);

            Entity entity = state.EntityManager.Instantiate(config.Prefab);

            state.EntityManager.SetComponentData(entity, new LocalTransform
            {
                Position = spawnPosition,
                Scale = 2f,
                Rotation = quaternion.LookRotationSafe(tangentDirection, normal)
            });

            state.EntityManager.AddComponentData(entity, new FollowTargetMovement
            {
                Target = playerEntity,
                Speed = 20f,
                StopDistance = 1f
            });
        }
    }
}
