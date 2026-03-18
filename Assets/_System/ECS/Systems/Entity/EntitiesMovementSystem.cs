using Unity.Collections.LowLevel.Unsafe;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Entities;
using Unity.Physics;
using Unity.Burst;
using Unity.Jobs;

/// <summary>
/// system for entity locomotion. Handles Linear, Follow, and Orbital movement patterns.
/// Supports two modes: "Snapped" (uses Physics Raycasts for terrain) and "Bare" (uses mathematical radius for perfect spheres).
/// </summary>
[UpdateInGroup(typeof(CustomUpdateGroup))]
[BurstCompile]
public partial struct EntitiesMovementSystem : ISystem
{
    private ComponentLookup<CoreStats> _statsLookup;
    private ComponentLookup<SteeringForce> _steeringLookup;
    private ComponentLookup<LocalTransform> _transformLookup;
    private ComponentLookup<StopDistance> _stopDistanceLookup;

    private ComponentLookup<Player> _playerLookup;
    private ComponentLookup<StunEffect> _stunLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        // Ensure the planet exists before attempting movement
        state.RequireForUpdate<PlanetData>();

        _statsLookup = state.GetComponentLookup<CoreStats>(true);
        _steeringLookup = state.GetComponentLookup<SteeringForce>(true);
        _transformLookup = state.GetComponentLookup<LocalTransform>(true);
        _playerLookup = state.GetComponentLookup<Player>(true);
        _stopDistanceLookup = state.GetComponentLookup<StopDistance>(true);

        _stunLookup = state.GetComponentLookup<StunEffect>(true);
    }

    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public void OnUpdate(ref SystemState state)
    {
        // Only update movement if the game is in a valid active state
        if (!SystemAPI.TryGetSingleton<GameState>(out var gameState))
            return;
        if (gameState.State != EGameState.Running && gameState.State != EGameState.Lobby)
            return;
        if (!SystemAPI.TryGetSingletonEntity<PlanetData>(out Entity planetEntity))
            return;

        var collisionWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld;
        var delta = SystemAPI.Time.DeltaTime;
        var planetTransform = SystemAPI.GetComponentRO<LocalTransform>(planetEntity).ValueRO;
        var planetData = SystemAPI.GetComponentRO<PlanetData>(planetEntity).ValueRO;

        // Refresh lookups for use in jobs
        _statsLookup.Update(ref state);
        _steeringLookup.Update(ref state);
        _transformLookup.Update(ref state);
        _playerLookup.Update(ref state);
        _stopDistanceLookup.Update(ref state);
        _stunLookup.Update(ref state);


        // Scheduled first cause just reading transform
        var updateOrbitCenterJob = new UpdateOrbitCenterPositionJob
        {
            LocalTransformLookup = _transformLookup
        };
        JobHandle orbitCenterHandle = updateOrbitCenterJob.ScheduleParallel(state.Dependency);


        // Schedule movement jobs

        var linearSnappedJob = new MoveLinearSnappedJob
        {
            DeltaTime = delta,
            PhysicsCollisionWorld = collisionWorld,
            PlanetCenter = planetTransform.Position,
            StatsLookup = _statsLookup,
            PlayerLookup = _playerLookup
        };
        JobHandle linearHandle = linearSnappedJob.ScheduleParallel(orbitCenterHandle);

        var followSnappedJob = new MoveFollowSnappedJob
        {
            PhysicsCollisionWorld = collisionWorld,
            DeltaTime = delta,
            PlanetCenter = planetTransform.Position,
            StatsLookup = _statsLookup,
            SteeringLookup = _steeringLookup,
            TransformLookup = _transformLookup,
            StopDistanceLookup = _stopDistanceLookup,
            StunLookup = _stunLookup
        };
        JobHandle followHandle = followSnappedJob.ScheduleParallel(orbitCenterHandle);

        var linearAndFollowHandle = JobHandle.CombineDependencies(linearHandle, followHandle);
        
        var orbitSnappedJob = new MoveOrbitSnappedJob
        {
            DeltaTime = delta,
            PlanetCenter = planetTransform.Position,
            PhysicsCollisionWorld = collisionWorld
        };
        JobHandle orbitSnappedHandle = orbitSnappedJob.ScheduleParallel(orbitCenterHandle);

        var orbitBareJob = new MoveOrbitBareJob
        {
            DeltaTime = delta,
            PlanetCenter = planetTransform.Position,
            PlanetRadius = planetData.Radius
        };
        JobHandle orbitBareHandle = orbitBareJob.ScheduleParallel(orbitCenterHandle);
        
        
        var orbitsHandle = JobHandle.CombineDependencies(orbitSnappedHandle, orbitBareHandle);
        
        state.Dependency = JobHandle.CombineDependencies(linearAndFollowHandle, orbitsHandle);
    }

    /// <summary>
    /// Moves entities in a straight line while using raycasts to snap them to uneven terrain.
    /// </summary>
    [BurstCompile]
    //[WithAll(typeof(LinearMovement) /*, typeof(HardSnappedMovement)*/)]
    [WithNone(typeof(FollowTargetMovement), typeof(OrbitMovement))]
    private partial struct MoveLinearSnappedJob : IJobEntity
    {
        [ReadOnly] public float DeltaTime;
        [ReadOnly] public CollisionWorld PhysicsCollisionWorld;
        [ReadOnly] public float3 PlanetCenter;

        [NativeDisableParallelForRestriction] [ReadOnly]
        public ComponentLookup<CoreStats> StatsLookup;

        [NativeDisableParallelForRestriction] [ReadOnly]
        public ComponentLookup<Player> PlayerLookup;

        private const float OBSTACLE_CHECK_DIST = 1.0f;

        private const float SNAP_DISTANCE = 500;

        private const float POS_SMOOTH_SPEED = 10.0f;
        private const float ROT_SMOOTH_SPEED = 15.0f;

        public void Execute(ref LocalTransform transform, in LinearMovement movement, Entity entity)
        {
            float speed = movement.Speed;
            if (StatsLookup.HasComponent(entity)) // if the entity has stats (player or enemy) use them
            {
                var stats = StatsLookup[entity];
                speed = stats.BaseMoveSpeed * stats.MoveSpeedMultiplier;
            }

            float3 currentNormal = math.normalize(transform.Position - PlanetCenter);

            if (PlanetUtils.SnapToSurfaceRaycast(ref PhysicsCollisionWorld, transform.Position, PlanetCenter,
                    new CollisionFilter
                        { BelongsTo = CollisionLayers.Raycast, CollidesWith = CollisionLayers.Landscape },
                    SNAP_DISTANCE, out Unity.Physics.RaycastHit currentHit))
            {
                currentNormal = currentHit.SurfaceNormal;
            }

            PlanetUtils.ProjectDirectionOnSurface(in movement.Direction, in currentNormal, out float3 tangentDirection);

            // Obstacle collision check (only for players)
            if (PlayerLookup.HasComponent(entity))
            {
                // If is moving
                if (math.lengthsq(tangentDirection) > 0.001f)
                {
                    var obstacleInput = new RaycastInput
                    {
                        Start = transform.Position + (currentNormal * 0.5f),
                        End = transform.Position + (currentNormal * 0.5f) + (tangentDirection * OBSTACLE_CHECK_DIST),
                        Filter = new CollisionFilter
                        {
                            BelongsTo = CollisionLayers.Raycast,
                            CollidesWith = CollisionLayers.Obstacle
                        }
                    };

                    if (PhysicsCollisionWorld.CastRay(obstacleInput, out var obstacleHit))
                    {
                        // Stop movement
                        // tangentDirection = float3.zero;

                        // Slide along the wall
                        float3 wallNormal = obstacleHit.SurfaceNormal;
                        tangentDirection =
                            tangentDirection -
                            wallNormal *
                            math.dot(tangentDirection, wallNormal); // project the tangent direction onto the wall plane
                    }
                }
            }

            float3 desiredPosition = transform.Position + tangentDirection * (speed * DeltaTime);

            if (PlanetUtils.SnapToSurfaceRaycast(
                    ref PhysicsCollisionWorld,
                    desiredPosition,
                    PlanetCenter,
                    new CollisionFilter
                    {
                        BelongsTo = CollisionLayers.Raycast,
                        CollidesWith = CollisionLayers.Landscape
                    },
                    SNAP_DISTANCE,
                    out Unity.Physics.RaycastHit hit))
            {
                float3 targetPosition = hit.Position;

                float3 delta = targetPosition - transform.Position;
                float maxStep = speed * DeltaTime * 1.2f;

                if (math.lengthsq(delta) > maxStep * maxStep)
                    delta = math.normalize(delta) * maxStep;

                transform.Position += delta;

                if (math.lengthsq(tangentDirection) > 0.001f)
                {
                    PlanetUtils.GetRotationOnSurface(
                        in tangentDirection,
                        hit.SurfaceNormal,
                        out quaternion targetRotation);

                    transform.Rotation = math.slerp(
                        transform.Rotation,
                        targetRotation,
                        DeltaTime * ROT_SMOOTH_SPEED
                    );
                }
            }
        }
    }

    /// <summary>
    /// Moves entities in a straight line, snapping them to a perfect sphere based on a fixed radius.
    /// </summary>
    [BurstCompile]
    [WithAll(typeof(LinearMovement))]
    [WithNone(typeof(HardSnappedMovement))]
    private partial struct MoveLinearBareJob : IJobEntity
    {
        [ReadOnly] public float DeltaTime;
        [ReadOnly] public float3 PlanetCenter;
        [ReadOnly] public float PlanetRadius;

        [NativeDisableParallelForRestriction] [ReadOnly]
        public ComponentLookup<CoreStats> StatsLookup;

        [NativeDisableParallelForRestriction] [ReadOnly]
        public ComponentLookup<Player> PlayerLookup;

        [ReadOnly] public CollisionWorld PhysicsCollisionWorld;

        private const float OBSTACLE_CHECK_DIST = 1.0f;

        public void Execute(ref LocalTransform transform, in LinearMovement movement, Entity entity)
        {
            float speed = movement.Speed;
            if (StatsLookup.HasComponent(entity))
            {
                var stats = StatsLookup[entity];
                speed = stats.BaseMoveSpeed * stats.MoveSpeedMultiplier;
            }

            PlanetUtils.GetSurfaceNormalRadius(transform.Position, PlanetCenter, out var currentNormal);
            PlanetUtils.ProjectDirectionOnSurface(in movement.Direction, in currentNormal, out float3 tangentDirection);

            // Obstacle collision check (only for players)
            //if (PlayerLookup.HasComponent(entity))
            //{
            //    if (math.lengthsq(tangentDirection) > 0.001f)
            //    {
            //        var obstacleInput = new RaycastInput
            //        {
            //            Start = transform.Position + (currentNormal * 0.5f),
            //            End = transform.Position + (currentNormal * 0.5f) + (tangentDirection * OBSTACLE_CHECK_DIST),
            //            Filter = new CollisionFilter
            //            {
            //                BelongsTo = CollisionLayers.Raycast,
            //                CollidesWith = CollisionLayers.Obstacle
            //            }
            //        };

            //        if (PhysicsCollisionWorld.CastRay(obstacleInput, out var obstacleHit))
            //        {
            //            // Stop movement
            //            // tangentDirection = float3.zero;

            //            // Slide along the wall
            //            float3 wallNormal = obstacleHit.SurfaceNormal;
            //            // Project the tangent direction onto the wall plane
            //            tangentDirection = tangentDirection - wallNormal * math.dot(tangentDirection, wallNormal);
            //        }
            //    }
            //}

            // Apply movement
            float3 newPosition = transform.Position + tangentDirection * (speed * DeltaTime);
            PlanetUtils.SnapToSurfaceRadius(newPosition, PlanetCenter, PlanetRadius, out var snapped);

            transform.Position = snapped;
            if (math.lengthsq(movement.Direction) > 0.001f)
            {
                PlanetUtils.GetSurfaceNormalRadius(transform.Position, PlanetCenter, out var n);
                PlanetUtils.GetRotationOnSurface(in tangentDirection, in n, out quaternion rotation);
                transform.Rotation = rotation;
            }
        }
    }

    /// <summary>
    /// Moves entities toward a target entity, incorporating steering forces and terrain snapping.
    /// </summary>
    [BurstCompile]
    [WithAll(typeof(FollowTargetMovement), typeof(HardSnappedMovement))]
    [WithNone(typeof(LinearMovement), typeof(OrbitMovement))]
    private partial struct MoveFollowSnappedJob : IJobEntity
    {
        [ReadOnly] public CollisionWorld PhysicsCollisionWorld;
        [ReadOnly] public float DeltaTime;
        [ReadOnly] public float3 PlanetCenter;

        [ReadOnly] public ComponentLookup<CoreStats> StatsLookup;
        [ReadOnly] public ComponentLookup<SteeringForce> SteeringLookup;
        [ReadOnly] public ComponentLookup<StopDistance> StopDistanceLookup;

        [NativeDisableContainerSafetyRestriction] [ReadOnly]
        public ComponentLookup<LocalTransform> TransformLookup;

        [ReadOnly] public ComponentLookup<StunEffect> StunLookup;

        private const float SNAP_DISTANCE = 10.0f;
        private const float POS_SMOOTH_SPEED = 25.0f;
        private const float ROT_SMOOTH_SPEED = 15.0f;


        public void Execute(ref LocalTransform transform, in FollowTargetMovement movement, Entity entity)
        {
            if (StunLookup.TryGetComponent(entity, out var _) && StunLookup.IsComponentEnabled(entity))
                return;

            float3 currentNormal = math.normalize(transform.Position - PlanetCenter);
            float3 unprojectedDirection;

            if (movement.Target != Entity.Null && TransformLookup.HasComponent(movement.Target))
            {
                float3 targetPosition = TransformLookup[movement.Target].Position;
                unprojectedDirection = targetPosition - transform.Position;
            }
            else
            {
                // if no more target, go forward
                unprojectedDirection = transform.Forward();
            }

            PlanetUtils.ProjectDirectionOnSurface(in unprojectedDirection, in currentNormal,
                out float3 tangentDirection);

            float3 steeringForce = float3.zero;
            if (SteeringLookup.HasComponent(entity))
                steeringForce = SteeringLookup[entity].Value;

            float3 finalDirection = tangentDirection + steeringForce;
            if (math.lengthsq(finalDirection) < 0.001f)
                finalDirection = transform.Forward();

            float stopDistance = 0;
            if (StopDistanceLookup.HasComponent(entity))
                stopDistance = StopDistanceLookup[entity].Distance;

            float distanceToTarget = math.length(unprojectedDirection);
            float stopFactor = distanceToTarget <= stopDistance && stopDistance > 0 ? 0 : 1; // 1: move, 0: stop

            // Within stopping distance, slow down
            if (distanceToTarget < stopDistance)
                finalDirection = math.normalize(finalDirection) * (distanceToTarget / stopDistance);

            float speed = movement.Speed;
            if (StatsLookup.HasComponent(entity))
            {
                var stats = StatsLookup[entity];
                speed = stats.BaseMoveSpeed * stats.MoveSpeedMultiplier;
            }

            float3 desiredPosition = transform.Position + (finalDirection * (speed * DeltaTime) * stopFactor);

            // Raycast
            var input = new RaycastInput
            {
                Start = desiredPosition + (currentNormal * SNAP_DISTANCE),
                End = desiredPosition - (currentNormal * SNAP_DISTANCE),
                Filter = new CollisionFilter
                    { BelongsTo = CollisionLayers.Raycast, CollidesWith = CollisionLayers.Landscape }
            };

            if (PhysicsCollisionWorld.CastRay(input, out var hit))
            {
                if (math.distancesq(transform.Position, hit.Position) > 1.0f)
                {
                    transform.Position = hit.Position;
                }
                else
                {
                    transform.Position = math.lerp(transform.Position, hit.Position, DeltaTime * POS_SMOOTH_SPEED);
                }

                PlanetUtils.GetRotationOnSurface(in finalDirection, hit.SurfaceNormal, out quaternion targetRotation);
                transform.Rotation = math.slerp(transform.Rotation, targetRotation, DeltaTime * ROT_SMOOTH_SPEED);
            }
            else
            {
                transform.Position = desiredPosition;
            }
        }
    }

    /// <summary>
    /// Moves entities toward a target entity, snapping to a perfect sphere radius.
    /// </summary>
    [BurstCompile]
    [WithAll(typeof(FollowTargetMovement))]
    [WithNone(typeof(HardSnappedMovement))]
    private partial struct MoveFollowBareJob : IJobEntity
    {
        [ReadOnly] public float DeltaTime;
        [ReadOnly] public float3 PlanetCenter;
        [ReadOnly] public float PlanetRadius;

        [ReadOnly] public ComponentLookup<CoreStats> StatsLookup;
        [ReadOnly] public ComponentLookup<SteeringForce> SteeringLookup;

        [NativeDisableContainerSafetyRestriction] [ReadOnly]
        public ComponentLookup<LocalTransform> TransformLookup;

        public void Execute(ref LocalTransform transform, in FollowTargetMovement movement, Entity entity)
        {
            if (movement.Target == Entity.Null || !TransformLookup.HasComponent(movement.Target))
                return;

            float3 targetPosition = TransformLookup[movement.Target].Position;
            float speed = movement.Speed;
            if (StatsLookup.HasComponent(entity))
            {
                var stats = StatsLookup[entity];
                speed = stats.BaseMoveSpeed * stats.MoveSpeedMultiplier;
            }

            PlanetUtils.GetSurfaceNormalRadius(in transform.Position, in PlanetCenter, out var normal);
            float3 directionToTarget = targetPosition - transform.Position;
            PlanetUtils.ProjectDirectionOnSurface(in directionToTarget, in normal, out float3 directionToPlayer);

            // Apply external steering
            float3 steeringForce = float3.zero;
            if (SteeringLookup.HasComponent(entity))
                steeringForce = SteeringLookup[entity].Value;

            float3 finalDirection = directionToPlayer + steeringForce;
            if (math.lengthsq(finalDirection) < 0.001f)
                finalDirection = transform.Forward();

            float3 newPosition = transform.Position + finalDirection * (speed * DeltaTime);
            PlanetUtils.SnapToSurfaceRadius(newPosition, PlanetCenter, PlanetRadius, out var snappedPosition);
            PlanetUtils.GetRotationOnSurface(in directionToPlayer, in normal, out quaternion rotation);

            transform.Position = snappedPosition;
            transform.Rotation = rotation;
        }
    }

    /// <summary>
    /// Rotates an entity around a center point while snapping to terrain.
    /// </summary>
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
            float3 orbitNormal = math.normalize(orbitCenterPosition - PlanetCenter);

            // Find the normal at the orbit center
            var input = new RaycastInput
            {
                Start = orbitCenterPosition + (orbitNormal * SNAP_DISTANCE),
                End = orbitCenterPosition - (orbitNormal * SNAP_DISTANCE),
                Filter = new CollisionFilter
                    { BelongsTo = CollisionLayers.Raycast, CollidesWith = CollisionLayers.Landscape }
            };

            if (PhysicsCollisionWorld.CastRay(input, out var centerHit))
                orbitNormal = centerHit.SurfaceNormal;

            // Rotate the relative offset around the center's normal
            quaternion rotation = quaternion.AxisAngle(orbitNormal, movement.AngularSpeed * DeltaTime);
            movement.RelativeOffset = math.mul(rotation, movement.RelativeOffset);
            movement.RelativeOffset = math.normalize(movement.RelativeOffset) * movement.Radius;
            float3 newOrbitPosition = orbitCenterPosition + movement.RelativeOffset;

            // Snap the final position to the ground
            input.Start = newOrbitPosition + (orbitNormal * SNAP_DISTANCE);
            input.End = newOrbitPosition - (orbitNormal * SNAP_DISTANCE);

            if (PhysicsCollisionWorld.CastRay(input, out var hit))
            {
                transform.Position = hit.Position;
                float3 tangentDirection = math.normalize(math.cross(hit.SurfaceNormal, orbitNormal));
                PlanetUtils.GetRotationOnSurface(in tangentDirection, hit.SurfaceNormal, out quaternion targetRotation);
                transform.Rotation = targetRotation;
            }
            else
            {
                transform.Position = newOrbitPosition;
            }
        }
    }

    /// <summary>
    /// Rotates an entity around a center point, snapping to a perfect sphere radius.
    /// </summary>
    [BurstCompile]
    [WithAll(typeof(OrbitMovement))]
    [WithNone(typeof(LinearMovement), typeof(FollowTargetMovement))]
    [WithNone(typeof(HardSnappedMovement))]
    private partial struct MoveOrbitBareJob : IJobEntity
    {
        [ReadOnly] public float DeltaTime;
        [ReadOnly] public float3 PlanetCenter;
        [ReadOnly] public float PlanetRadius;

        public void Execute(ref LocalTransform transform, ref OrbitMovement movement)
        {
            if (movement.OrbitCenterEntity == Entity.Null) return;
            float3 orbitCenterPosition = movement.OrbitCenterPosition;
            PlanetUtils.GetSurfaceNormalRadius(in orbitCenterPosition, in PlanetCenter, out float3 orbitNormal);

            quaternion rotation = quaternion.AxisAngle(orbitNormal, movement.AngularSpeed * DeltaTime);

            movement.RelativeOffset = math.mul(rotation, movement.RelativeOffset);
            movement.RelativeOffset = math.normalize(movement.RelativeOffset) * movement.Radius;
            float3 newOrbitPosition = orbitCenterPosition + movement.RelativeOffset;

            PlanetUtils.SnapToSurfaceRadius(in newOrbitPosition, in PlanetCenter, PlanetRadius,
                out float3 snappedPosition);
            transform.Position = snappedPosition;
            PlanetUtils.GetSurfaceNormalRadius(in snappedPosition, in PlanetCenter, out float3 normal);
            float3 tangentDirection = math.normalize(math.cross(normal, orbitNormal));
            PlanetUtils.GetRotationOnSurface(in tangentDirection, in normal, out quaternion targetRotation);
            transform.Rotation = targetRotation;
        }
    }

    /// <summary>
    /// Updates the cached world position of an orbit center entity.
    /// </summary>
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