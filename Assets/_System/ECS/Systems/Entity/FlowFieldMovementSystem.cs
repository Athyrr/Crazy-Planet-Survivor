using _System.Settings;
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

    /// <summary> Fallback rotation smoothing used when no CpBaseEnemySettings asset is available. </summary>
    private const float DefaultRotationLerpSpeed = 10f;

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

    // Not Burst-compiled: reads the managed CpBaseEnemySettings asset so the rotation smoothing
    // is tunable live in the inspector. The heavy per-entity work stays in the Burst job below.
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
        var planetData = SystemAPI.GetSingleton<PlanetData>();
        var collisionWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld;

        // Live-tunable rotation smoothing (falls back to the default if no settings asset exists).
        float rotationLerpSpeed = CpBaseEnemySettings.I != null
            ? CpBaseEnemySettings.RotationLerpSpeed
            : DefaultRotationLerpSpeed;

        _steeringLookup.Update(ref state);
        _statsLookup.Update(ref state);
        _stunLookup.Update(ref state);
        _stopDistanceLookup.Update(ref state);
        _cellBufferLookup.Update(ref state);

        var moveJob = new MoveFlowFieldSnappedJob
        {
            DeltaTime = SystemAPI.Time.DeltaTime,
            PlanetCenter = planetData.Center,
            PhysicsCollisionWorld = collisionWorld,
            FlowField = flowFieldData,
            FlowFieldEntity = flowFieldEntity,
            CellBufferLookup = _cellBufferLookup,
            SteeringLookup = _steeringLookup,
            StatsLookup = _statsLookup,
            StunLookup = _stunLookup,
            StopDistanceLookup = _stopDistanceLookup,
            RotationLerpSpeed = rotationLerpSpeed
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

        /// <summary> Rotation smoothing factor (lerp speed), sourced from CpBaseEnemySettings. </summary>
        [ReadOnly] public float RotationLerpSpeed;

        private const float SnapDistance = 500f;
        private const float VertSnapSpeed = 20.0f;
        // Avoidance is already in world-space units; cap its contribution so it
        // doesn't overpower the flow-field direction on its own.
        private const float SteeringBlend = 0.35f;
        // Just outside the stop ring the entity decelerates over a band of
        // (StopDistance * StopSlowBandFraction) units so it eases in ("adapts its speed")
        // instead of slamming to a halt.
        private const float StopSlowBandFraction = 0.5f;

        public void Execute(Entity entity, ref LocalTransform transform)
        {
            // Stunned entities do not move
            if (StunLookup.TryGetComponent(entity, out var _) && StunLookup.IsComponentEnabled(entity))
                return;

            float3 currentNormal = math.normalize(transform.Position - PlanetCenter);

            // --- Direction straight to the player (goal), projected onto the surface. ---
            // This is the orientation target: looking at the player is stable, whereas the
            // flow/avoidance direction is noisy and makes packed entities spin in place.
            float3 toGoal = FlowField.Origin - transform.Position;
            PlanetUtils.ProjectDirectionOnSurface(in toGoal, in currentNormal, out float3 goalDirection);
            bool hasGoalDirection = math.lengthsq(goalDirection) > 0.0001f;

            // --- Sample flow field (bilinear) ---
            float3 flowDirection = SampleFlowField(transform.Position);
            if (math.lengthsq(flowDirection) < 0.001f)
                flowDirection = transform.Forward();
            else
                flowDirection = math.normalize(flowDirection);

            // --- Blend avoidance steering (capped so it never fully overrides flow) ---
            float3 steeringForce = float3.zero;
            if (SteeringLookup.HasComponent(entity))
                steeringForce = SteeringLookup[entity].Value;

            float3 desiredDirection = flowDirection + steeringForce * SteeringBlend;
            bool hasMoveDirection = math.lengthsq(desiredDirection) > 0.001f;
            desiredDirection = hasMoveDirection ? math.normalize(desiredDirection) : transform.Forward();

            // --- Stop distance: decelerate toward the ring, then hold position. ---
            // Inside the ring the entity has "arrived": it stops advancing instead of creeping
            // all the way onto the player. Just outside, speed ramps up so it eases in.
            float speedFactor = 1f;
            if (StopDistanceLookup.HasComponent(entity))
            {
                float stopDist = StopDistanceLookup[entity].Distance;
                if (stopDist > 0f)
                {
                    float distToGoal = math.distance(transform.Position, FlowField.Origin);
                    if (distToGoal <= stopDist)
                    {
                        speedFactor = 0f;
                    }
                    else
                    {
                        float slowBand = math.max(stopDist * StopSlowBandFraction, 0.0001f);
                        speedFactor = math.saturate((distToGoal - stopDist) / slowBand);
                    }
                }
            }

            // --- Orientation: always look toward the player, never toward the avoidance steering. ---
            // The steering force flips direction constantly when entities are packed together, so
            // driving rotation from it makes a (near-)stationary entity spin in place. Facing the
            // player directly is stable and is what we want regardless of how it shuffles for spacing.
            float3 faceDirection = hasGoalDirection ? goalDirection : flowDirection;

            // Movement still follows the flow field + avoidance so entities path in and keep spacing.
            float3 moveDirection = desiredDirection;

            // --- Speed ---
            float speed = 3f;
            if (StatsLookup.HasComponent(entity))
            {
                var stats = StatsLookup[entity];
                speed = stats.BaseMoveSpeed * (1f + stats.MoveSpeed);
            }

            float3 desiredPosition = transform.Position + moveDirection * (speed * speedFactor * DeltaTime);

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
                // Decompose the delta into horizontal (tangential) and vertical (normal) parts.
                // Apply full horizontal movement; smooth only the vertical (terrain-following) component
                // to avoid jarring jumps over bumpy terrain.
                float3 toHit = hit.Position - transform.Position;
                float verticalDelta = math.dot(toHit, currentNormal);
                float3 horizontalDelta = toHit - currentNormal * verticalDelta;

                // Snap immediately for large gaps (spawning/teleport), smooth for small terrain bumps
                float absVert = math.abs(verticalDelta);
                float vertSmooth = absVert > 1.5f
                    ? verticalDelta
                    : verticalDelta * math.min(1f, DeltaTime * VertSnapSpeed);

                transform.Position += horizontalDelta + currentNormal * vertSmooth;

                PlanetUtils.GetRotationOnSurface(in faceDirection, hit.SurfaceNormal, out quaternion targetRotation);
                transform.Rotation = math.slerp(transform.Rotation, targetRotation, DeltaTime * RotationLerpSpeed);
            }
            else
            {
                transform.Position = desiredPosition;

                // Keep turning to face the player even when off the navigable mesh.
                PlanetUtils.GetRotationOnSurface(in faceDirection, currentNormal, out quaternion targetRotation);
                transform.Rotation = math.slerp(transform.Rotation, targetRotation, DeltaTime * RotationLerpSpeed);
            }
        }

        /// <summary>
        /// Projects the entity's world position onto the grid and returns a bilinearly
        /// interpolated flow direction across the four surrounding cells.
        /// Blocked cells (cost=255) contribute zero, so entities near walls are smoothly
        /// steered away rather than receiving a hard direction flip.
        /// Returns float3.zero when fully outside the grid or the buffer is unavailable.
        /// </summary>
        private float3 SampleFlowField(float3 worldPos)
        {
            if (!CellBufferLookup.HasBuffer(FlowFieldEntity))
                return float3.zero;

            var cells = CellBufferLookup[FlowFieldEntity];

            float3 offset = worldPos - FlowField.Origin;
            float localX = math.dot(offset, FlowField.GridRight);
            float localZ = math.dot(offset, FlowField.GridForward);

            // Fractional grid coordinates (cell centers are at integer positions)
            float fx = localX / FlowField.CellSize + FlowField.GridWidth  * 0.5f - 0.5f;
            float fz = localZ / FlowField.CellSize + FlowField.GridHeight * 0.5f - 0.5f;

            int x0 = (int)math.floor(fx);
            int z0 = (int)math.floor(fz);
            float tx = fx - x0;
            float tz = fz - z0;

            float3 d00 = GetCellDir(in cells, x0,     z0);
            float3 d10 = GetCellDir(in cells, x0 + 1, z0);
            float3 d01 = GetCellDir(in cells, x0,     z0 + 1);
            float3 d11 = GetCellDir(in cells, x0 + 1, z0 + 1);

            float3 dir = math.lerp(math.lerp(d00, d10, tx), math.lerp(d01, d11, tx), tz);
            return math.lengthsq(dir) > 0.001f ? dir : float3.zero;
        }

        /// <summary>
        /// Returns the stored direction for a cell, or float3.zero for out-of-bounds and blocked cells.
        /// </summary>
        private float3 GetCellDir(in DynamicBuffer<FlowFieldCell> cells, int cx, int cz)
        {
            if (cx < 0 || cx >= FlowField.GridWidth || cz < 0 || cz >= FlowField.GridHeight)
                return float3.zero;
            var cell = cells[cz * FlowField.GridWidth + cx];
            return cell.Cost == byte.MaxValue ? float3.zero : cell.Direction;
        }
    }
}
