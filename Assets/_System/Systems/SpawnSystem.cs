using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
public partial struct SpawnerSystem : ISystem
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

        Random rand = new(45619); // Random seed 

        // Stop after the first tick
        state.Enabled = false;

        for (int i = 0; i < config.Amount; i++)
        {
            // Random spawn position
            float3 randPosInCircle = rand.NextFloat3Direction() * rand.NextFloat();
            float3 randPosition = math.normalize(randPosInCircle);
            float3 spawnPosition = planetCenter + randPosition * planetRadius;

            // Planet normal at position
            float3 normal = math.normalize(randPosition - planetCenter);

            // Random projected direction
            float3 randDirInCircle = rand.NextFloat3Direction() * rand.NextFloat();
            float3 randDirection = math.normalize(randDirInCircle);
            float3 projected = randDirection - math.dot(randDirection, normal) * normal;
            float3 tangentDirection = math.normalize(projected);

            Entity entity = state.EntityManager.Instantiate(config.Prefab);

            state.EntityManager.SetComponentData(entity, new LocalTransform()
            {
                Position = spawnPosition,
                Scale = 2,
                Rotation = quaternion.LookRotationSafe(tangentDirection, normal)
            });

            if (SystemAPI.HasComponent<Velocity>(entity))
            {
                var speed = SystemAPI.GetComponent<Velocity>(entity).Magnitude;
                state.EntityManager.SetComponentData(entity, new Velocity()
                {
                    Direction = tangentDirection,
                    Magnitude = speed,
                });
            }
            else
            {
                state.EntityManager.AddComponentData(entity, new Velocity()
                {
                    Direction = tangentDirection,
                    Magnitude = 1 // @todo create an Entity config to set value
                });
            }
        }
    }
}
