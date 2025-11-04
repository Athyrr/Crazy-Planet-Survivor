using Unity.Collections;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Entities;
using Unity.Physics;
using Unity.Burst;
using Unity.Jobs;

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

        var collisionWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld;

        var delta = SystemAPI.Time.DeltaTime;
        var planetTransform = SystemAPI.GetComponentRO<LocalTransform>(planetEntity).ValueRO;
        var planetData = SystemAPI.GetComponentRO<PlanetData>(planetEntity).ValueRO;

        bool playerExists = SystemAPI.TryGetSingletonEntity<Player>(out var player);
        LocalTransform playerTransform = playerExists ? SystemAPI.GetComponentRO<LocalTransform>(player).ValueRO : default;
        float3 playerPosition = playerExists ? playerTransform.Position : float3.zero;

        ComponentLookup<Stats> statsLookup = SystemAPI.GetComponentLookup<Stats>(true);

        // Linear snapped movement job
        var linearSnappedJob = new MoveLinearSnappedJob
        {
            DeltaTime = delta,
            PhysicsCollisionWorld = collisionWorld,
            PlanetCenter = planetTransform.Position,
            StatsLookup = statsLookup
        };
        JobHandle linearSnappedHandle = linearSnappedJob.ScheduleParallel(state.Dependency);

        // Linear snapped movement job
        var linearBareJob = new MoveLinearBareJob
        {
            DeltaTime = delta,
            PlanetCenter = planetTransform.Position,
            PlanetRadius = planetData.Radius,
            StatsLookup = statsLookup
        };
        JobHandle linearBareHandle = linearBareJob.ScheduleParallel(linearSnappedHandle);

        // Combine linear movement handles
        JobHandle linearHandle = JobHandle.CombineDependencies(linearSnappedHandle, linearBareHandle);

        // Follow movement job
        JobHandle followHandle = linearBareHandle;
        if (playerExists)
        {
            var followSnappedJob = new MoveFollowSnappedJob
            {
                PhysicsCollisionWorld = collisionWorld,
                PlayerPosition = playerPosition,
                DeltaTime = delta,
                PlanetCenter = planetTransform.Position,
                StatsLookup = statsLookup
            };
            JobHandle followSnappedHandle = followSnappedJob.ScheduleParallel(linearHandle);

            var followBareJob = new MoveFollowBareJob 
            {
                PlayerPosition = playerPosition,
                DeltaTime = delta,
                PlanetCenter = planetTransform.Position,
                PlanetRadius = planetData.Radius,
                StatsLookup = statsLookup
            };
            JobHandle followBareHandle = followBareJob.ScheduleParallel(linearHandle);

            // Combine follow movement handles
            followHandle = JobHandle.CombineDependencies(followSnappedHandle, followBareHandle);
        }

        // Update orbit center position job
        var updateOrbitCenterJob = new UpdateOrbitCenterPositionJob
        {
            LocalTransformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true)
        };
        JobHandle updateOrbitCenterHandle = updateOrbitCenterJob.ScheduleParallel(followHandle);

        // Orbital movement job
        var orbitSnappedJob = new MoveOrbitSnappedJob
        {
            DeltaTime = delta,
            PlanetCenter = planetTransform.Position,
            PhysicsCollisionWorld = collisionWorld
        };
        JobHandle orbitSnappedHandle = orbitSnappedJob.ScheduleParallel(updateOrbitCenterHandle);

        var orbitBareJob = new MoveOrbitBareJob 
        {
            DeltaTime = delta,
            PlanetCenter = planetTransform.Position,
            PlanetRadius = planetData.Radius
        };
        JobHandle orbitBareHandle = orbitBareJob.ScheduleParallel(updateOrbitCenterHandle);

        // Combine orbit movement handles
        state.Dependency = JobHandle.CombineDependencies(orbitSnappedHandle, orbitBareHandle);
        //state.Dependency = orbitBareHandle;
    }

    #region Jobs


    [BurstCompile]
    [WithAll(typeof(LinearMovement), typeof(HardSnappedMovement))]
    [WithNone(typeof(FollowTargetMovement), typeof(OrbitMovement))]
    private partial struct MoveLinearSnappedJob : IJobEntity
    {
        [ReadOnly] public float DeltaTime;
        [ReadOnly] public CollisionWorld PhysicsCollisionWorld;
        [ReadOnly] public PlanetData PlanetData;
        [ReadOnly] public float3 PlanetCenter;
        [ReadOnly] public float PlanetRadius;
        [ReadOnly] public ComponentLookup<Stats> StatsLookup;

        private const float SNAP_DISTANCE = 10f;

        public void Execute(ref LocalTransform transform, in LinearMovement movement, Entity entity)
        {
            float speed = StatsLookup.HasComponent(entity) ? StatsLookup[entity].Speed : movement.Speed;

            float3 currentNormal;
            if (PlanetMovementUtils.SnapToSurfaceRaycast(
                    ref PhysicsCollisionWorld,
                    transform.Position, PlanetCenter,
                    new CollisionFilter { BelongsTo = CollisionLayers.Raycast, CollidesWith = CollisionLayers.Landscape },
                    SNAP_DISTANCE,
                    out Unity.Physics.RaycastHit currentHit))
            {
                currentNormal = currentHit.SurfaceNormal;
            }
            else // Fallback
            {
                currentNormal = math.normalize(transform.Position - PlanetCenter);
            }

            // Calculate the tangent direction
            PlanetMovementUtils.ProjectDirectionOnSurface(in movement.Direction, in currentNormal, out float3 tangentDirection);
            float3 newPosition = transform.Position + tangentDirection * (speed * DeltaTime);

            // Snap
            if (PlanetMovementUtils.SnapToSurfaceRaycast(
                        ref PhysicsCollisionWorld,
                        newPosition,
                        PlanetCenter,
                        new CollisionFilter { BelongsTo = CollisionLayers.Raycast, CollidesWith = CollisionLayers.Landscape },
                        SNAP_DISTANCE,
                        out Unity.Physics.RaycastHit hit))
            {
                transform.Position = hit.Position;

                if (math.lengthsq(movement.Direction) > 0.001f)
                {
                    var n = hit.SurfaceNormal;
                    PlanetMovementUtils.GetRotationOnSurface(in tangentDirection, in n, out quaternion rotation);
                    transform.Rotation = rotation;
                }
            }
            else
            {
                // Fallback if no ground found
                transform.Position = newPosition;
            }
        }
    }

    [WithAll(typeof(LinearMovement))]
    [WithNone(typeof(FollowTargetMovement), typeof(OrbitMovement), typeof(HardSnappedMovement))]
    private partial struct MoveLinearBareJob : IJobEntity
    {
        [ReadOnly] public float DeltaTime;
        [ReadOnly] public PlanetData PlanetData;
        [ReadOnly] public float3 PlanetCenter;
        [ReadOnly] public float PlanetRadius;
        [ReadOnly] public ComponentLookup<Stats> StatsLookup;

        public void Execute(ref LocalTransform transform, in LinearMovement movement, Entity entity)
        {
            float speed = StatsLookup.HasComponent(entity) ? StatsLookup[entity].Speed : movement.Speed;

            // Get current normal
            PlanetMovementUtils.GetSurfaceNormalRadius(transform.Position, PlanetCenter, out var currentNormal);

            // Calculate the tangent direction
            PlanetMovementUtils.ProjectDirectionOnSurface(in movement.Direction, in currentNormal, out float3 tangentDirection);
            float3 newPosition = transform.Position + tangentDirection * (speed * DeltaTime);

            PlanetMovementUtils.SnapToSurfaceRadius(newPosition, PlanetCenter, PlanetRadius, out var snapped);
            transform.Position = snapped;
            if (math.lengthsq(movement.Direction) > 0.001f)
            {
                PlanetMovementUtils.GetSurfaceNormalRadius(transform.Position, PlanetCenter, out var n);
                PlanetMovementUtils.GetRotationOnSurface(in tangentDirection, in n, out quaternion rotation);
                transform.Rotation = rotation;
            }
        }
    }

    [BurstCompile]
    [WithAll(typeof(FollowTargetMovement), typeof(HardSnappedMovement))]
    [WithNone(typeof(LinearMovement), typeof(OrbitMovement))]
    private partial struct MoveFollowSnappedJob : IJobEntity
    {
        [ReadOnly] public CollisionWorld PhysicsCollisionWorld;
        [ReadOnly] public float DeltaTime;
        [ReadOnly] public float3 PlayerPosition;
        [ReadOnly] public float3 PlanetCenter;
        [ReadOnly] public ComponentLookup<Stats> StatsLookup;

        private const float SNAP_DISTANCE = 10.0f;

        public void Execute(ref LocalTransform transform, in FollowTargetMovement movement, Entity entity)
        {
            float speed = StatsLookup.HasComponent(entity) ? StatsLookup[entity].Speed : movement.Speed;
            float3 currentNrmal;
            if (PlanetMovementUtils.SnapToSurfaceRaycast(ref PhysicsCollisionWorld, transform.Position, PlanetCenter,
                    new CollisionFilter { BelongsTo = CollisionLayers.Raycast, CollidesWith = CollisionLayers.Landscape }, // Votre nouveau filtre est bon
                    SNAP_DISTANCE, out Unity.Physics.RaycastHit currentHit))
            { currentNrmal = currentHit.SurfaceNormal; }
            else { currentNrmal = math.normalize(transform.Position - PlanetCenter); }

            float3 directionToPlayer = PlayerPosition - transform.Position;
            PlanetMovementUtils.ProjectDirectionOnSurface(in directionToPlayer, in currentNrmal, out float3 tangentDirection);
            if (math.lengthsq(tangentDirection) < 0.001f) tangentDirection = transform.Forward();
            float3 newPosition = transform.Position + tangentDirection * (speed * DeltaTime);

            if (PlanetMovementUtils.SnapToSurfaceRaycast(ref PhysicsCollisionWorld, newPosition, PlanetCenter,
                    new CollisionFilter { BelongsTo = CollisionLayers.Raycast, CollidesWith = CollisionLayers.Landscape },
                    SNAP_DISTANCE, out Unity.Physics.RaycastHit hit))
            {
                transform.Position = hit.Position;
                var n = hit.SurfaceNormal;
                PlanetMovementUtils.GetRotationOnSurface(in tangentDirection, in n, out quaternion rotation);
                transform.Rotation = rotation;
            }
            else { transform.Position = newPosition; }
        }
    }

    [BurstCompile]
    [WithAll(typeof(FollowTargetMovement))]
    [WithNone(typeof(LinearMovement), typeof(OrbitMovement), typeof(HardSnappedMovement))]
    private partial struct MoveFollowBareJob : IJobEntity
    {
        [ReadOnly] public float DeltaTime;
        [ReadOnly] public float3 PlayerPosition;
        [ReadOnly] public float3 PlanetCenter;
        [ReadOnly] public float PlanetRadius;
        [ReadOnly] public ComponentLookup<Stats> StatsLookup;

        public void Execute(ref LocalTransform transform, in FollowTargetMovement movement, Entity entity)
        {
            float speed = StatsLookup.HasComponent(entity) ? StatsLookup[entity].Speed : movement.Speed;

            PlanetMovementUtils.GetSurfaceNormalRadius(in transform.Position, in PlanetCenter, out var normal);
            PlanetMovementUtils.GetSurfaceStepTowardPositionRadius(in transform.Position, in PlayerPosition, speed * DeltaTime, in PlanetCenter, PlanetRadius, out float3 targetPosition);

            float3 actualDirection = math.normalize(targetPosition - transform.Position);
            if (math.lengthsq(actualDirection) < 0.001f)
                actualDirection = transform.Forward();

            PlanetMovementUtils.GetRotationOnSurface(in actualDirection, in normal, out quaternion rotation);

            transform.Position = targetPosition;
            transform.Rotation = rotation;
        }
    }

    [BurstCompile]
    [WithAll(typeof(OrbitMovement), typeof(HardSnappedMovement))] 
    [WithNone(typeof(LinearMovement), typeof(FollowTargetMovement))]
    private partial struct MoveOrbitSnappedJob : IJobEntity
    {
        [ReadOnly] public float DeltaTime;
        [ReadOnly] public float3 PlanetCenter;
        [ReadOnly] public CollisionWorld PhysicsCollisionWorld;
        private const float SNAP_DISTANCE = 10.0f;

        public void Execute(ref LocalTransform transform, ref OrbitMovement movement)
        {
            if (movement.OrbitCenterEntity == Entity.Null) return;
            float3 orbitCenterPosition = movement.OrbitCenterPosition;
            float3 orbitNormal;
            if (PlanetMovementUtils.SnapToSurfaceRaycast(ref PhysicsCollisionWorld, orbitCenterPosition, PlanetCenter,
                    new CollisionFilter { BelongsTo = CollisionLayers.Raycast, CollidesWith = CollisionLayers.Landscape },
                    SNAP_DISTANCE, out Unity.Physics.RaycastHit centerHit))
            { orbitNormal = centerHit.SurfaceNormal; }
            else { orbitNormal = math.normalize(orbitCenterPosition - PlanetCenter); }

            quaternion rotation = quaternion.AxisAngle(orbitNormal, movement.AngularSpeed * DeltaTime);
            movement.RelativeOffset = math.mul(rotation, movement.RelativeOffset);
            movement.RelativeOffset = math.normalize(movement.RelativeOffset) * movement.Radius;
            float3 newOrbitPosition = orbitCenterPosition + movement.RelativeOffset;

            if (PlanetMovementUtils.SnapToSurfaceRaycast(ref PhysicsCollisionWorld, newOrbitPosition, PlanetCenter,
                    new CollisionFilter { BelongsTo = CollisionLayers.Raycast, CollidesWith = CollisionLayers.Landscape },
                    SNAP_DISTANCE, out Unity.Physics.RaycastHit hit))
            {
                transform.Position = hit.Position;
                var n = hit.SurfaceNormal;
                float3 tangentDirection = math.normalize(math.cross(n, orbitNormal));
                PlanetMovementUtils.GetRotationOnSurface(in tangentDirection, in n, out quaternion targetRotation);
                transform.Rotation = targetRotation;
            }
            else { transform.Position = newOrbitPosition; }
        }
    }

    [BurstCompile]
    [WithAll(typeof(OrbitMovement))]
    [WithNone(typeof(LinearMovement), typeof(FollowTargetMovement), typeof(HardSnappedMovement))] 
    private partial struct MoveOrbitBareJob : IJobEntity
    {
        [ReadOnly] public float DeltaTime;
        [ReadOnly] public float3 PlanetCenter;
        [ReadOnly] public float PlanetRadius;

        public void Execute(ref LocalTransform transform, ref OrbitMovement movement)
        {
            if (movement.OrbitCenterEntity == Entity.Null)
                return;

            float3 orbitCenterPosition = movement.OrbitCenterPosition;

            PlanetMovementUtils.GetSurfaceNormalRadius(in orbitCenterPosition, in PlanetCenter, out float3 orbitNormal);
            quaternion rotation = quaternion.AxisAngle(orbitNormal, movement.AngularSpeed * DeltaTime);

            movement.RelativeOffset = math.mul(rotation, movement.RelativeOffset);
            movement.RelativeOffset = math.normalize(movement.RelativeOffset) * movement.Radius;

            float3 newOrbitPosition = orbitCenterPosition + movement.RelativeOffset;

            PlanetMovementUtils.SnapToSurfaceRadius(in newOrbitPosition, in PlanetCenter, PlanetRadius, out float3 snappedPosition);
            transform.Position = snappedPosition;

            PlanetMovementUtils.GetSurfaceNormalRadius(in snappedPosition, in PlanetCenter, out float3 normal);
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

    #endregion
}