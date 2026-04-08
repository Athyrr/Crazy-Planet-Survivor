using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Physics;
using Unity.Burst;

/// <summary>
/// Moves entities that have FlowFieldFollowerMovement by sampling the FlowFieldData singleton.
/// Intended as an alternative to FollowTargetMovement — entities should have one or the other, not both.
/// Runs after AvoidanceSystem so SteeringForce values are already populated.
/// </summary>
[UpdateInGroup(typeof(CustomUpdateGroup))]
[UpdateAfter(typeof(AvoidanceSystem))]
[BurstCompile]
public partial struct FlowFieldMovementSystem : ISystem
{
    private ComponentLookup<SteeringForce> _steeringLookup;
    private ComponentLookup<CoreStats> _statsLookup;
    private ComponentLookup<StunEffect> _stunLookup;
    private ComponentLookup<StopDistance> _stopDistanceLookup;
    private BufferLookup<FlowFieldCell> _cellBufferLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<FlowFieldData>();
        state.RequireForUpdate<PlanetData>();
        state.RequireForUpdate<GameState>();

        _steeringLookup = state.GetComponentLookup<SteeringForce>(isReadOnly: true);
        _statsLookup = state.GetComponentLookup<CoreStats>(isReadOnly: true);
        _stunLookup = state.GetComponentLookup<StunEffect>(isReadOnly: true);
        _stopDistanceLookup = state.GetComponentLookup<StopDistance>(isReadOnly: true);
        _cellBufferLookup = state.GetBufferLookup<FlowFieldCell>(isReadOnly: true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton<GameState>(out var gameState))
            return;
        if (gameState.State != EGameState.Running && gameState.State != EGameState.Lobby)
            return;

        var flowFieldData = SystemAPI.GetSingleton<FlowFieldData>();
        if (!flowFieldData.IsReady)
            return;

        var flowFieldEntity = SystemAPI.GetSingletonEntity<FlowFieldData>();
        var planetTransform = SystemAPI.GetComponentRO<LocalTransform>(SystemAPI.GetSingletonEntity<PlanetData>()).ValueRO;
        var collisionWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld;

        _steeringLookup.Update(ref state);
        _statsLookup.Update(ref state);
        _stunLookup.Update(ref state);
        _stopDistanceLookup.Update(ref state);
        _cellBufferLookup.Update(ref state);

        var moveJob = new MoveFlowFieldSnappedJob
        {
            DeltaTime = SystemAPI.Time.DeltaTime,
            PlanetCenter = planetTransform.Position,
            PhysicsCollisionWorld = collisionWorld,
            FlowField = flowFieldData,
            FlowFieldEntity = flowFieldEntity,
            CellBufferLookup = _cellBufferLookup,
            SteeringLookup = _steeringLookup,
            StatsLookup = _statsLookup,
            StunLookup = _stunLookup,
            StopDistanceLookup = _stopDistanceLookup
        };
        state.Dependency = moveJob.ScheduleParallel(state.Dependency);
    }

    /// <summary>
    /// Samples the flow field to determine movement direction, then snaps the entity to terrain via raycast.
    /// Steering forces from AvoidanceSystem are added on top of the flow field direction.
    /// </summary>
    [BurstCompile]
    [WithAll(typeof(FlowFieldFollowerMovement), typeof(HardSnappedMovement))]
    private partial struct MoveFlowFieldSnappedJob : IJobEntity
    {
        [ReadOnly] public float DeltaTime;
        [ReadOnly] public float3 PlanetCenter;
        [ReadOnly] public CollisionWorld PhysicsCollisionWorld;
        [ReadOnly] public FlowFieldData FlowField;
        [ReadOnly] public Entity FlowFieldEntity;

        [ReadOnly] public BufferLookup<FlowFieldCell> CellBufferLookup;
        [ReadOnly] public ComponentLookup<SteeringForce> SteeringLookup;
        [ReadOnly] public ComponentLookup<CoreStats> StatsLookup;
        [ReadOnly] public ComponentLookup<StunEffect> StunLookup;
        [ReadOnly] public ComponentLookup<StopDistance> StopDistanceLookup;

        private const float SnapDistance = 500f;
        private const float PosLerpSpeed = 25.0f;
        private const float RotLerpSpeed = 15.0f;

        public void Execute(Entity entity, ref LocalTransform transform, in FlowFieldFollowerMovement _)
        {
            // Stunned entities do not move
            if (StunLookup.TryGetComponent(entity, out var _) && StunLookup.IsComponentEnabled(entity))
                return;

            // --- Sample flow field ---
            float3 flowDirection = SampleFlowField(transform.Position);

            if (math.lengthsq(flowDirection) < 0.001f)
                flowDirection = transform.Forward();

            // --- Add avoidance steering ---
            float3 steeringForce = float3.zero;
            if (SteeringLookup.HasComponent(entity))
                steeringForce = SteeringLookup[entity].Value;

            float3 finalDirection = flowDirection + steeringForce;
            if (math.lengthsq(finalDirection) < 0.001f)
                finalDirection = transform.Forward();

            // --- Speed ---
            float speed = 3f;
            if (StatsLookup.HasComponent(entity))
            {
                var stats = StatsLookup[entity];
                speed = stats.BaseMoveSpeed * stats.MoveSpeedMultiplier;
            }

            // --- Stop distance (uses Euclidean distance to the flow field goal/player) ---
            if (StopDistanceLookup.HasComponent(entity))
            {
                float stopDist = StopDistanceLookup[entity].Distance;
                if (stopDist > 0f)
                {
                    float distToGoal = math.distance(transform.Position, FlowField.Origin);
                    if (distToGoal <= stopDist)
                    {
                        float t = math.saturate(distToGoal / stopDist);
                        finalDirection = math.normalize(finalDirection) * t;
                        if (t < 0.05f)
                            return;
                    }
                }
            }

            float3 currentNormal = math.normalize(transform.Position - PlanetCenter);
            float3 desiredPosition = transform.Position + finalDirection * (speed * DeltaTime);

            // --- Raycast terrain snap ---
            var input = new RaycastInput
            {
                Start = desiredPosition + currentNormal * SnapDistance,
                End = desiredPosition - currentNormal * SnapDistance,
                Filter = new CollisionFilter
                {
                    BelongsTo = CollisionLayers.Raycast,
                    CollidesWith = CollisionLayers.Landscape
                }
            };

            if (PhysicsCollisionWorld.CastRay(input, out var hit))
            {
                if (math.distancesq(transform.Position, hit.Position) > 1.0f)
                    transform.Position = hit.Position;
                else
                    transform.Position = math.lerp(transform.Position, hit.Position, DeltaTime * PosLerpSpeed);

                PlanetUtils.GetRotationOnSurface(in finalDirection, hit.SurfaceNormal, out quaternion targetRotation);
                transform.Rotation = math.slerp(transform.Rotation, targetRotation, DeltaTime * RotLerpSpeed);
            }
            else
            {
                transform.Position = desiredPosition;
            }
        }

        /// <summary>
        /// Projects the entity's world position onto the grid and returns the stored flow direction.
        /// Returns float3.zero when outside the grid bounds or the buffer is unavailable.
        /// </summary>
        private float3 SampleFlowField(float3 worldPos)
        {
            if (!CellBufferLookup.HasBuffer(FlowFieldEntity))
                return float3.zero;

            float3 offset = worldPos - FlowField.Origin;
            float localX = math.dot(offset, FlowField.GridRight);
            float localZ = math.dot(offset, FlowField.GridForward);

            int cx = (int)math.round(localX / FlowField.CellSize) + FlowField.GridWidth / 2;
            int cy = (int)math.round(localZ / FlowField.CellSize) + FlowField.GridHeight / 2;

            if (cx < 0 || cx >= FlowField.GridWidth || cy < 0 || cy >= FlowField.GridHeight)
                return float3.zero;

            var cells = CellBufferLookup[FlowFieldEntity];
            int idx = cy * FlowField.GridWidth + cx;
            if (idx < 0 || idx >= cells.Length)
                return float3.zero;

            return cells[idx].Direction;
        }
    }
}
