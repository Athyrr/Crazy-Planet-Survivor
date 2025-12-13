using Unity.Collections.LowLevel.Unsafe;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Entities;
using Unity.Physics;
using Unity.Burst;
using Unity.Jobs;

//[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateInGroup(typeof(TestUpdateGroup))]
[BurstCompile]
public partial struct EntitiesMovementSystem : ISystem
{
    [ReadOnly] private ComponentLookup<Stats> _statsLookup;
    [ReadOnly] private ComponentLookup<SteeringForce> _steeringLookup;
    [ReadOnly] private ComponentLookup<LocalTransform> _transformLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PlanetData>();

        _statsLookup = state.GetComponentLookup<Stats>(true);
        _steeringLookup = state.GetComponentLookup<SteeringForce>(true);
        _transformLookup = state.GetComponentLookup<LocalTransform>(true);
    }

    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public void OnUpdate(ref SystemState state)
    {
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

        //ComponentLookup<Stats> statsLookup = SystemAPI.GetComponentLookup<Stats>(true);
        //ComponentLookup<SteeringForce> steeringLookup = SystemAPI.GetComponentLookup<SteeringForce>(true);
        //ComponentLookup<LocalTransform> transformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true);

        _statsLookup.Update(ref state);
        _steeringLookup.Update(ref state);
        _transformLookup.Update(ref state);

        // Linear snapped movement job
        var linearSnappedJob = new MoveLinearSnappedJob
        {
            DeltaTime = delta,
            PhysicsCollisionWorld = collisionWorld,
            PlanetCenter = planetTransform.Position,
            StatsLookup = _statsLookup
        };
        JobHandle linearSnappedHandle = linearSnappedJob.ScheduleParallel(state.Dependency);

        // Linear snapped movement job
        var linearBareJob = new MoveLinearBareJob
        {
            DeltaTime = delta,
            PlanetCenter = planetTransform.Position,
            PlanetRadius = planetData.Radius,
            StatsLookup = _statsLookup
        };
        JobHandle linearBareHandle = linearBareJob.ScheduleParallel(linearSnappedHandle);

        // Combine linear movement handles
        JobHandle linearHandle = JobHandle.CombineDependencies(linearSnappedHandle, linearBareHandle);

        // Follow movement job
        var followSnappedJob = new MoveFollowSnappedJob
        {
            PhysicsCollisionWorld = collisionWorld,
            DeltaTime = delta,
            PlanetCenter = planetTransform.Position,
            StatsLookup = _statsLookup,
            SteeringLookup = _steeringLookup,
            TransformLookup = _transformLookup
        };
        JobHandle followSnappedHandle = followSnappedJob.ScheduleParallel(linearHandle);

        var followBareJob = new MoveFollowBareJob
        {
            DeltaTime = delta,
            PlanetCenter = planetTransform.Position,
            PlanetRadius = planetData.Radius,
            StatsLookup = _statsLookup,
            SteeringLookup = _steeringLookup,
            TransformLookup = _transformLookup
        };
        JobHandle followBareHandle = followBareJob.ScheduleParallel(followSnappedHandle);

        // Combine follow movement handles
        var followHandle = JobHandle.CombineDependencies(followSnappedHandle, followBareHandle);

        // Update orbit center position job
        var updateOrbitCenterJob = new UpdateOrbitCenterPositionJob
        {
            //LocalTransformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true)
            LocalTransformLookup = _transformLookup
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
        JobHandle orbitBareHandle = orbitBareJob.ScheduleParallel(orbitSnappedHandle);

        // Combine orbit movement handles
        state.Dependency = JobHandle.CombineDependencies(orbitSnappedHandle, orbitBareHandle);
        //state.Dependency = orbitBareHandle;
    }

    #region Jobs

    [BurstCompile]
    [WithAll(typeof(LinearMovement), typeof(HardSnappedMovement))]
    //[WithNone(typeof(FollowTargetMovement), typeof(OrbitMovement))]
    private partial struct MoveLinearSnappedJob : IJobEntity
    {
        [ReadOnly] public float DeltaTime;
        [ReadOnly] public CollisionWorld PhysicsCollisionWorld;
        [ReadOnly] public PlanetData PlanetData;
        [ReadOnly] public float3 PlanetCenter;
        [ReadOnly] public float PlanetRadius;

        [NativeDisableParallelForRestriction]
        [ReadOnly] public ComponentLookup<Stats> StatsLookup;

        private const float SNAP_DISTANCE = 10f;

        public void Execute(ref LocalTransform transform, in LinearMovement movement, Entity entity)
        {
            float speed = StatsLookup.HasComponent(entity) ? StatsLookup[entity].MoveSpeed : movement.Speed;

            float3 currentNormal;
            if (PlanetUtils.SnapToSurfaceRaycast(
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
            PlanetUtils.ProjectDirectionOnSurface(in movement.Direction, in currentNormal, out float3 tangentDirection);
            float3 newPosition = transform.Position + tangentDirection * (speed * DeltaTime);

            // Snap
            if (PlanetUtils.SnapToSurfaceRaycast(
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
                    PlanetUtils.GetRotationOnSurface(in tangentDirection, in n, out quaternion rotation);
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

    [BurstCompile]
    [WithAll(typeof(LinearMovement))]
    //[WithNone(typeof(FollowTargetMovement), typeof(OrbitMovement))]
    [WithNone(typeof(HardSnappedMovement))]
    private partial struct MoveLinearBareJob : IJobEntity
    {
        [ReadOnly] public float DeltaTime;
        [ReadOnly] public PlanetData PlanetData;
        [ReadOnly] public float3 PlanetCenter;
        [ReadOnly] public float PlanetRadius;

        [NativeDisableParallelForRestriction]
        [ReadOnly] public ComponentLookup<Stats> StatsLookup;

        public void Execute(ref LocalTransform transform, in LinearMovement movement, Entity entity)
        {
            float speed = StatsLookup.HasComponent(entity) ? StatsLookup[entity].MoveSpeed : movement.Speed;

            // Get current normal
            PlanetUtils.GetSurfaceNormalRadius(transform.Position, PlanetCenter, out var currentNormal);

            // Calculate the tangent direction
            PlanetUtils.ProjectDirectionOnSurface(in movement.Direction, in currentNormal, out float3 tangentDirection);
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

    [BurstCompile]
    [WithAll(typeof(FollowTargetMovement), typeof(HardSnappedMovement))]
    //[WithNone(typeof(LinearMovement), typeof(OrbitMovement))]
    private partial struct MoveFollowSnappedJob : IJobEntity
    {
        [ReadOnly] public CollisionWorld PhysicsCollisionWorld;
        [ReadOnly] public float DeltaTime;
        [ReadOnly] public float3 PlanetCenter;

        [ReadOnly] public ComponentLookup<Stats> StatsLookup;
        [ReadOnly] public ComponentLookup<SteeringForce> SteeringLookup;

        [NativeDisableContainerSafetyRestriction]
        [ReadOnly] public ComponentLookup<LocalTransform> TransformLookup;

        private const float SNAP_DISTANCE = 10.0f;

        public void Execute(ref LocalTransform transform, in FollowTargetMovement movement, Entity entity)
        {
            if (movement.Target == Entity.Null || !TransformLookup.HasComponent(movement.Target))
                return;

            float3 targetPosition = TransformLookup[movement.Target].Position;

            float speed = StatsLookup.HasComponent(entity) ? StatsLookup[entity].MoveSpeed : movement.Speed;

            float3 currentNormal;
            if (PlanetUtils.SnapToSurfaceRaycast(ref PhysicsCollisionWorld, transform.Position, PlanetCenter,
                    new CollisionFilter { BelongsTo = CollisionLayers.Raycast, CollidesWith = CollisionLayers.Landscape }, // Votre nouveau filtre est bon
                    SNAP_DISTANCE, out Unity.Physics.RaycastHit currentHit))
            { currentNormal = currentHit.SurfaceNormal; }
            else { currentNormal = math.normalize(transform.Position - PlanetCenter); }

            float3 directionToPlayer = targetPosition - transform.Position;
            PlanetUtils.ProjectDirectionOnSurface(in directionToPlayer, in currentNormal, out float3 tangentDirection);

            float3 steeringForce = float3.zero;
            if (SteeringLookup.HasComponent(entity))
                steeringForce = SteeringLookup[entity].Value;

            // Combine direction with steering
            float3 finalDirection = tangentDirection + steeringForce;

            if (math.lengthsq(finalDirection) < 0.001f)
                finalDirection = transform.Forward();

            // Calculate new position
            float3 newPosition = transform.Position + finalDirection * (speed * DeltaTime);

            // Snap to surface
            if (PlanetUtils.SnapToSurfaceRaycast(ref PhysicsCollisionWorld, newPosition, PlanetCenter,
                            new CollisionFilter { BelongsTo = CollisionLayers.Raycast, CollidesWith = CollisionLayers.Landscape },
                            SNAP_DISTANCE, out Unity.Physics.RaycastHit hit))
            {
                transform.Position = hit.Position;
                var n = hit.SurfaceNormal;

                // Rotate to face movement direction
                PlanetUtils.GetRotationOnSurface(in directionToPlayer, in n, out quaternion rotation);
                transform.Rotation = rotation;
            }
            else
            {
                transform.Position = newPosition;
            }
        }
    }

    [BurstCompile]
    [WithAll(typeof(FollowTargetMovement))]
    //[WithNone(typeof(LinearMovement), typeof(OrbitMovement))]
    [WithNone(typeof(HardSnappedMovement))]
    private partial struct MoveFollowBareJob : IJobEntity
    {
        [ReadOnly] public float DeltaTime;
        [ReadOnly] public float3 PlanetCenter;
        [ReadOnly] public float PlanetRadius;

        [ReadOnly] public ComponentLookup<Stats> StatsLookup;
        [ReadOnly] public ComponentLookup<SteeringForce> SteeringLookup;

        [NativeDisableContainerSafetyRestriction]
        [ReadOnly] public ComponentLookup<LocalTransform> TransformLookup;

        public void Execute(ref LocalTransform transform, in FollowTargetMovement movement, Entity entity)
        {
            if (movement.Target == Entity.Null || !TransformLookup.HasComponent(movement.Target))
                return;

            float3 targetPosition = TransformLookup[movement.Target].Position;

            float speed = StatsLookup.HasComponent(entity) ? StatsLookup[entity].MoveSpeed : movement.Speed;

            PlanetUtils.GetSurfaceNormalRadius(in transform.Position, in PlanetCenter, out var normal);

            float3 directionToTarget = targetPosition - transform.Position;
            PlanetUtils.ProjectDirectionOnSurface(in directionToTarget, in normal, out float3 directionToPlayer);

            float3 steeringForce = float3.zero;
            if (SteeringLookup.HasComponent(entity))
                steeringForce = SteeringLookup[entity].Value;

            // Combine direction with steering
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
            if (PlanetUtils.SnapToSurfaceRaycast(ref PhysicsCollisionWorld, orbitCenterPosition, PlanetCenter,
                    new CollisionFilter { BelongsTo = CollisionLayers.Raycast, CollidesWith = CollisionLayers.Landscape },
                    SNAP_DISTANCE, out Unity.Physics.RaycastHit centerHit))
            { orbitNormal = centerHit.SurfaceNormal; }
            else { orbitNormal = math.normalize(orbitCenterPosition - PlanetCenter); }

            quaternion rotation = quaternion.AxisAngle(orbitNormal, movement.AngularSpeed * DeltaTime);
            movement.RelativeOffset = math.mul(rotation, movement.RelativeOffset);
            movement.RelativeOffset = math.normalize(movement.RelativeOffset) * movement.Radius;
            float3 newOrbitPosition = orbitCenterPosition + movement.RelativeOffset;

            if (PlanetUtils.SnapToSurfaceRaycast(ref PhysicsCollisionWorld, newOrbitPosition, PlanetCenter,
                    new CollisionFilter { BelongsTo = CollisionLayers.Raycast, CollidesWith = CollisionLayers.Landscape },
                    SNAP_DISTANCE, out Unity.Physics.RaycastHit hit))
            {
                transform.Position = hit.Position;
                var n = hit.SurfaceNormal;
                float3 tangentDirection = math.normalize(math.cross(n, orbitNormal));
                PlanetUtils.GetRotationOnSurface(in tangentDirection, in n, out quaternion targetRotation);
                transform.Rotation = targetRotation;
            }
            else { transform.Position = newOrbitPosition; }
        }
    }

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
            if (movement.OrbitCenterEntity == Entity.Null)
                return;

            float3 orbitCenterPosition = movement.OrbitCenterPosition;

            PlanetUtils.GetSurfaceNormalRadius(in orbitCenterPosition, in PlanetCenter, out float3 orbitNormal);
            quaternion rotation = quaternion.AxisAngle(orbitNormal, movement.AngularSpeed * DeltaTime);

            movement.RelativeOffset = math.mul(rotation, movement.RelativeOffset);
            movement.RelativeOffset = math.normalize(movement.RelativeOffset) * movement.Radius;

            float3 newOrbitPosition = orbitCenterPosition + movement.RelativeOffset;

            PlanetUtils.SnapToSurfaceRadius(in newOrbitPosition, in PlanetCenter, PlanetRadius, out float3 snappedPosition);
            transform.Position = snappedPosition;

            PlanetUtils.GetSurfaceNormalRadius(in snappedPosition, in PlanetCenter, out float3 normal);
            float3 tangentDirection = math.normalize(math.cross(normal, orbitNormal));
            PlanetUtils.GetRotationOnSurface(in tangentDirection, in normal, out quaternion targetRotation);
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