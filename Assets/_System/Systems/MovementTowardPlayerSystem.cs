using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;

[UpdateAfter(typeof(SpawnerSystem))]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[BurstCompile]
public partial struct MovementTowardPlayerSystem : ISystem
{
    private Entity _planetEntity;
    private Entity _playerEntity;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PlanetData>();
        state.RequireForUpdate<Player>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingletonEntity<PlanetData>(out _planetEntity))
            return;

        if (!SystemAPI.TryGetSingletonEntity<Player>(out _playerEntity))
            return;

        float delta = SystemAPI.Time.DeltaTime;

        LocalTransform playerTransform = SystemAPI.GetComponentRO<LocalTransform>(_playerEntity).ValueRO;

        LocalTransform planetTransform = SystemAPI.GetComponentRO<LocalTransform>(_planetEntity).ValueRO;
        PlanetData planetData = SystemAPI.GetComponentRO<PlanetData>(_planetEntity).ValueRO;

        var moveJob = new MoveEnemiesOnPlanetJob
        {
            PlayerPosition = playerTransform.Position,
            PlanetCenter = planetTransform.Position,
            PlanetRadius = planetData.Radius,
            Delta = delta
        };

        state.Dependency = moveJob.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    private partial struct MoveEnemiesOnPlanetJob : IJobEntity
    {
        public float3 PlayerPosition;
        public float3 PlanetCenter;
        public float PlanetRadius;
        public float Delta;

        void Execute(ref LocalTransform transform, ref Velocity velocity, in Enemy enemy)
        {
            float3 position = transform.Position;
            float3 direction = math.normalize(PlayerPosition - position);
            //float3 direction = math.normalize(velocity.Direction);
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
        }
    }
}

