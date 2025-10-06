using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

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
            PlanetRadius = planetData.Radius
        };
        // Execute on threads 
        JobHandle linearHandle = linearJob.ScheduleParallel(state.Dependency);

        // Follow movement job
        JobHandle followHandle = default;
        if (playerExists)
        {
            var followJob = new MoveFollowJob
            {
                playerPosition = playerPos,
                deltaTime = delta,
                PlanetCenter = planetTransform.Position,
                PlanetRadius = planetData.Radius
            };
            followHandle = followJob.ScheduleParallel(linearHandle);
        }

        // Orbital movement job
        var orbitalMovementJob = new OrbitMovementJob
        {
            DeltaTime = delta,
            PlanetCenter = planetTransform.Position,
            PlanetRadius = planetData.Radius
        };
        JobHandle orbitHandle = orbitalMovementJob.ScheduleParallel(followHandle);

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

        public void Execute(ref LocalTransform transform, ref LinearMovement movement)
        {
            if (math.lengthsq(movement.Direction) < 0.001f)
                return;

            PlanetMovementUtils.GetSurfaceNormalAtPosition(in transform.Position, in PlanetCenter, out var normal);
            PlanetMovementUtils.ProjectDirectionOnSurface(in movement.Direction, in normal, out float3 tangentDirection);

            float3 newPosition = transform.Position + tangentDirection * (movement.Speed * deltaTime);
            PlanetMovementUtils.SnapToSurface(in newPosition, in PlanetCenter, PlanetRadius, out float3 snappedPosition);

            PlanetMovementUtils.GetRotationOnSurface(in tangentDirection, in normal, out quaternion rotation);

            movement.Direction = tangentDirection;
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

        public void Execute(ref LocalTransform transform, in FollowTargetMovement movement)
        {
            PlanetMovementUtils.GetSurfaceNormalAtPosition(in transform.Position, in PlanetCenter, out var normal);
            PlanetMovementUtils.GetSurfaceStepTowardPosition(in transform.Position, in playerPosition, movement.Speed * deltaTime, in PlanetCenter, PlanetRadius, out float3 targetPosition);

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
    partial struct OrbitMovementJob : IJobEntity
    {
        [ReadOnly] public float DeltaTime;
        [ReadOnly] public float3 PlanetCenter;
        [ReadOnly] public float PlanetRadius;

        void Execute(ref LocalTransform transform, in OrbitMovement movement)
        {
            float3 orbitCenter = movement.OrbitCenter;

            float3 orbitVector = transform.Position - orbitCenter;

            PlanetMovementUtils.GetSurfaceNormalAtPosition(in orbitCenter, in PlanetCenter, out float3 orbitNormal);
            quaternion rotation = quaternion.AxisAngle(orbitNormal, movement.AngularSpeed * DeltaTime);

            float3 rotatedVector = math.mul(rotation, orbitVector);
            float3 newOrbitPosition = orbitCenter + rotatedVector;

            float3 newPosition = PlanetCenter + math.normalize(newOrbitPosition - PlanetCenter) * PlanetRadius;

            PlanetMovementUtils.GetSurfaceNormalAtPosition(in newPosition, in PlanetCenter, out float3 normal);
            float3 tangentDirection = math.normalize(math.cross(normal, orbitNormal));

            transform.Position = newPosition;
            PlanetMovementUtils.GetRotationOnSurface(in tangentDirection, in normal, out quaternion targetRotation);
            transform.Rotation = targetRotation;
        }
    }
}

