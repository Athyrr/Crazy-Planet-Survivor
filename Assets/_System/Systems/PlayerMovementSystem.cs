using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[BurstCompile]
public partial struct PlayerMovementSystem : ISystem
{
    private Entity _planetEntity;

    private float3 _randDir;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Player>();
        state.RequireForUpdate<PlanetData>();

        uint seed = (uint)SystemAPI.Time.ElapsedTime + 15668489;
        Random rand = new(seed);
        _randDir = math.normalize(rand.NextFloat3Direction() * rand.NextFloat());
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingletonEntity<PlanetData>(out _planetEntity))
            return;

        float delta = SystemAPI.Time.DeltaTime;

        LocalTransform planetTransform = SystemAPI.GetComponentRO<LocalTransform>(_planetEntity).ValueRO;
        PlanetData planetData = SystemAPI.GetComponentRO<PlanetData>(_planetEntity).ValueRO;

        var moveJob = new MovePlayerOnPlanetJob()
        {
            PlanetCenter = planetTransform.Position,
            PlanetRadius = planetData.Radius,
            Delta = delta,
            Dir = _randDir
        };

        state.Dependency = moveJob.ScheduleParallel(state.Dependency);
    }

    private partial struct MovePlayerOnPlanetJob : IJobEntity
    {
        public float3 PlanetCenter;
        public float PlanetRadius;
        public float Delta;
        public float3 Dir;

        void Execute(ref LocalTransform transform, ref Velocity velocity, in Player player)
        {
            float3 position = transform.Position;
            float3 direction = Dir;
            float speed = velocity.Magnitude;

            // Get normal at player position
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
