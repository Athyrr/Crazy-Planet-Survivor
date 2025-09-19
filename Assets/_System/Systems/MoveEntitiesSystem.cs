using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;

[UpdateAfter(typeof(EnemiesMovementRequestProviderSystem))]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[BurstCompile]
public partial struct MoveEntitiesSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PlanetData>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingletonEntity<PlanetData>(out Entity planetEntity))
            return;

        float delta = SystemAPI.Time.DeltaTime;

        var ecbSystem = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        EntityCommandBuffer.ParallelWriter ecb = ecbSystem.CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter();

        LocalTransform planetTransform = SystemAPI.GetComponentRO<LocalTransform>(planetEntity).ValueRO;
        PlanetData planetData = SystemAPI.GetComponentRO<PlanetData>(planetEntity).ValueRO;

        var moveJob = new MoveEntitiesOnPlanetJob
        {
            PlanetCenter = planetTransform.Position,
            PlanetRadius = planetData.Radius,
            Delta = delta,
            ECB = ecb
        };

        state.Dependency = moveJob.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    private partial struct MoveEntitiesOnPlanetJob : IJobEntity
    {
        public float3 PlanetCenter;
        public float PlanetRadius;
        public float Delta;

        public EntityCommandBuffer.ParallelWriter ECB;

        void Execute([EntityIndexInQuery] int index, Entity entity, ref LocalTransform transform, ref Velocity velocity, in RequestForMovement request)
        {
            float3 position = transform.Position;
            float3 direction = request.Direction;
            float speed = velocity.Magnitude;

            // Get normal at entity position
            float3 normal = math.normalize(position - PlanetCenter);

            // Project direction on surface
            float3 tangentDirection = direction - math.dot(direction, normal) * normal;
            tangentDirection = math.normalize(tangentDirection);

            // Calculate target projected position
            float3 targetPosition = position + tangentDirection * speed * Delta;

            // Snap to surface
            float3 snappedPosition = PlanetCenter + math.normalize(targetPosition - PlanetCenter) * PlanetRadius; // @todo sample height map 

            // Rotation
            quaternion rotation = quaternion.LookRotationSafe(tangentDirection, normal);

            // Apply new direction
            velocity.Direction = tangentDirection;

            // Apply new transform
            transform = new LocalTransform
            {
                Position = snappedPosition,
                Rotation = rotation,
                Scale = transform.Scale
            };

            // Remove request
            ECB.RemoveComponent<RequestForMovement>(index, entity);
        }
    }
}

