using Unity.Jobs;
using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;
using Unity.Collections;

/// <summary>
/// System that processes all entity movements on a planetary surface.
/// <para>
/// - <see cref="MoveLinearJob"/>: Moves entities along a fixed direction.
/// - <see cref="MoveFollowJob"/>: Moves entities towards the player's position.
/// - <see cref="OrbitMovementJob"/>: Moves entities in a circular orbit around a central point.
/// </para>
/// <para>
/// All calculations leverage the <see cref="PlanetMovementUtils"/> helper to ensure entities correctly follow the planet's curvature.
/// </para>
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[BurstCompile]
public partial struct EntitiesMovementSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PlanetData>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton<GameState>(out var gameState))
            return;

        if (gameState.State != EGameState.Running)
            return;

        if (!SystemAPI.TryGetSingletonEntity<PlanetData>(out Entity planetEntity))
            return;

        var delta = SystemAPI.Time.DeltaTime;
        var planetTransform = SystemAPI.GetComponentRO<LocalTransform>(planetEntity).ValueRO;
        var planetData = SystemAPI.GetComponentRO<PlanetData>(planetEntity).ValueRO;

        bool playerExists = SystemAPI.TryGetSingletonEntity<Player>(out var player);
        LocalTransform playerTransform = playerExists ? SystemAPI.GetComponentRO<LocalTransform>(player).ValueRO : default;
        float3 playerPos = playerExists ? playerTransform.Position : float3.zero;


        // Linear movement job
        var linearJob = new MoveLinearJob
        {
            deltaTime = delta,
            PlanetCenter = planetTransform.Position,
            PlanetRadius = planetData.Radius,
            StatsLookup = SystemAPI.GetComponentLookup<Stats>(true)
        };
        // Execute on threads 
        JobHandle linearHandle = linearJob.ScheduleParallel(state.Dependency);


        // Follow movement job
        JobHandle followHandle = linearHandle;
        if (playerExists)
        {
            var followJob = new MoveFollowJob
            {
                playerPosition = playerPos,
                deltaTime = delta,
                PlanetCenter = planetTransform.Position,
                PlanetRadius = planetData.Radius,
                StatsLookup = SystemAPI.GetComponentLookup<Stats>(true)
            };
            followHandle = followJob.ScheduleParallel(linearHandle);
        }


        // Update orbit center position job
        var updateOrbitCenterJob = new UpdateOrbitCenterPositionJob
        {
            LocalTransformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true)
        };
        JobHandle updateOrbitCenterHandle = updateOrbitCenterJob.ScheduleParallel(followHandle);


        // Orbital movement job
        var orbitalMovementJob = new OrbitMovementJob
        {
            DeltaTime = delta,
            PlanetCenter = planetTransform.Position,
            PlanetRadius = planetData.Radius
        };
        JobHandle orbitHandle = orbitalMovementJob.ScheduleParallel(updateOrbitCenterHandle);


        // Final dependency
        state.Dependency = orbitHandle;
    }


    [BurstCompile]
    [WithAll(typeof(LinearMovement))]
    [WithNone(typeof(FollowTargetMovement), typeof(OrbitMovement))]
    private partial struct MoveLinearJob : IJobEntity
    {
        [ReadOnly] public float deltaTime;
        [ReadOnly] public float3 PlanetCenter;
        [ReadOnly] public float PlanetRadius;

        [ReadOnly] public ComponentLookup<Stats> StatsLookup;

        public void Execute(ref LocalTransform transform, in LinearMovement movement, Entity entity)
        {
            if (math.lengthsq(movement.Direction) < 0.001f)
                return;

            float speed = StatsLookup.HasComponent(entity) ? StatsLookup[entity].Speed : movement.Speed;

            PlanetMovementUtils.GetSurfaceNormalAtPosition(in transform.Position, in PlanetCenter, out var normal);
            PlanetMovementUtils.ProjectDirectionOnSurface(in movement.Direction, in normal, out float3 tangentDirection);

            float3 newPosition = transform.Position + tangentDirection * (speed * deltaTime);
            PlanetMovementUtils.SnapToSurface(in newPosition, in PlanetCenter, PlanetRadius, out float3 snappedPosition);

            PlanetMovementUtils.GetRotationOnSurface(in tangentDirection, in normal, out quaternion rotation);

            //movement.Direction = tangentDirection;
            transform.Position = snappedPosition;
            transform.Rotation = rotation;
        }
    }


    [BurstCompile]
    [WithAll(typeof(FollowTargetMovement))]
    [WithNone(typeof(LinearMovement), typeof(OrbitMovement))]
    private partial struct MoveFollowJob : IJobEntity
    {
        [ReadOnly] public float deltaTime;
        [ReadOnly] public float3 playerPosition;
        [ReadOnly] public float3 PlanetCenter;
        [ReadOnly] public float PlanetRadius;

        [ReadOnly] public ComponentLookup<Stats> StatsLookup;

        public void Execute(ref LocalTransform transform, in FollowTargetMovement movement, Entity entity)
        {
            float speed = StatsLookup.HasComponent(entity) ? StatsLookup[entity].Speed : movement.Speed;

            PlanetMovementUtils.GetSurfaceNormalAtPosition(in transform.Position, in PlanetCenter, out var normal);
            PlanetMovementUtils.GetSurfaceStepTowardPosition(in transform.Position, in playerPosition, speed * deltaTime, in PlanetCenter, PlanetRadius, out float3 targetPosition);

            float3 actualDirection = math.normalize(targetPosition - transform.Position);
            if (math.lengthsq(actualDirection) < 0.001f) actualDirection = transform.Forward();

            PlanetMovementUtils.GetRotationOnSurface(in actualDirection, in normal, out quaternion rotation);

            transform.Position = targetPosition;
            transform.Rotation = rotation;
        }
    }


    [BurstCompile]
    [WithAll(typeof(OrbitMovement))]
    [WithNone(typeof(LinearMovement), typeof(FollowTargetMovement))]
    private partial struct OrbitMovementJob : IJobEntity
    {
        [ReadOnly] public float DeltaTime;
        [ReadOnly] public float3 PlanetCenter;
        [ReadOnly] public float PlanetRadius;

        public void Execute(ref LocalTransform transform, ref OrbitMovement movement)
        {
            if (movement.OrbitCenterEntity == Entity.Null)
                return;

            Entity orbitCenterEntity = movement.OrbitCenterEntity;
            float3 orbitCenterPosition = movement.OrbitCenterPosition;

            PlanetMovementUtils.GetSurfaceNormalAtPosition(in orbitCenterPosition, in PlanetCenter, out float3 orbitNormal);
            quaternion rotation = quaternion.AxisAngle(orbitNormal, movement.AngularSpeed * DeltaTime);

            movement.RelativeOffset = math.mul(rotation, movement.RelativeOffset);
            movement.RelativeOffset = math.normalize(movement.RelativeOffset) * movement.Radius;

            float3 newOrbitPosition = orbitCenterPosition + movement.RelativeOffset;

            PlanetMovementUtils.SnapToSurface(in newOrbitPosition, in PlanetCenter, PlanetRadius, out float3 snappedPosition);
            transform.Position = snappedPosition;

            // Calculate the rotation to face the movement direction
            PlanetMovementUtils.GetSurfaceNormalAtPosition(in snappedPosition, in PlanetCenter, out float3 normal);
            float3 tangentDirection = math.normalize(math.cross(normal, orbitNormal));
            PlanetMovementUtils.GetRotationOnSurface(in tangentDirection, in normal, out quaternion targetRotation);
            transform.Rotation = targetRotation;
        }
    }


    [BurstCompile]
    [WithAll(typeof(OrbitMovement))]
    [WithNone(typeof(LinearMovement), typeof(FollowTargetMovement))]
    private partial struct UpdateOrbitCenterPositionJob : IJobEntity
    {
        [ReadOnly] public ComponentLookup<LocalTransform> LocalTransformLookup;

        public void Execute(ref OrbitMovement movement)
        {
            if (LocalTransformLookup.HasComponent(movement.OrbitCenterEntity))
            {
                movement.OrbitCenterPosition = LocalTransformLookup[movement.OrbitCenterEntity].Position;
            }
        }
    }
}

