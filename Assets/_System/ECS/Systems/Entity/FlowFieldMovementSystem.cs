using _System.Settings;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Physics;
using Unity.Burst;

/// <summary>
/// Moves entities that have FlowFieldFollowerMovement: samples the flow-field grid and the avoidance
/// steering, accelerates a persistent velocity toward the result, and snaps the entity to the surface.
/// Alternative to FollowTargetMovement (an entity has one or the other, not both). Runs every frame so
/// motion is smooth, consuming the grid/steering that CustomUpdateGroup recomputes at a lower rate.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(CustomUpdateGroup))]
[UpdateAfter(typeof(ActiveEffectsSystem))]
[UpdateBefore(typeof(TransformSystemGroup))]
[BurstCompile]
public partial struct FlowFieldMovementSystem : ISystem
{
    private ComponentLookup<SteeringForce> _steeringLookup;
    private ComponentLookup<Avoidance> _avoidanceLookup;
    private ComponentLookup<FinalStats> _finalStatsLookup;
    private ComponentLookup<StunEffect> _stunLookup;
    private ComponentLookup<ActiveKnockback> _knockbackLookup;
    private ComponentLookup<StopDistance> _stopDistanceLookup;
    private BufferLookup<FlowFieldCell> _cellBufferLookup;

    /// <summary> Fallbacks used when no CpBaseEnemySettings asset is available. </summary>
    private const float DefaultAcceleration = 40f;
    private const float DefaultMaxTurnRateDeg = 540f;
    private const float DefaultFacingSpeedThreshold = 0.5f;
    private const float DefaultMassAccelerationInfluence = 0.35f;
    private const float DefaultMassTurnRateInfluence = 0.35f;
    private const float DefaultReferenceMass = 1f;
    private const float DefaultMinMassFactor = 0.1f;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PhysicsWorldSingleton>();
        state.RequireForUpdate<FlowFieldData>();
        state.RequireForUpdate<PlanetData>();
        state.RequireForUpdate<GameState>();

        _steeringLookup = state.GetComponentLookup<SteeringForce>(isReadOnly: true);
        _avoidanceLookup = state.GetComponentLookup<Avoidance>(isReadOnly: true);
        _finalStatsLookup = state.GetComponentLookup<FinalStats>(isReadOnly: true);
        _stunLookup = state.GetComponentLookup<StunEffect>(isReadOnly: true);
        _knockbackLookup = state.GetComponentLookup<ActiveKnockback>(isReadOnly: true);
        _stopDistanceLookup = state.GetComponentLookup<StopDistance>(isReadOnly: true);
        _cellBufferLookup = state.GetBufferLookup<FlowFieldCell>(isReadOnly: true);
    }

    // Not Burst-compiled: reads the managed CpBaseEnemySettings for live-tunable movement feel. The
    // per-entity work stays in the Burst job below, which receives plain floats.
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

        // Live-tunable movement feel (falls back to the defaults if no settings asset exists).
        bool hasSettings = CpBaseEnemySettings.I != null;
        float globalAcceleration = hasSettings ? CpBaseEnemySettings.Acceleration : DefaultAcceleration;
        float globalMaxTurnRateDeg = hasSettings ? CpBaseEnemySettings.MaxTurnRateDeg : DefaultMaxTurnRateDeg;
        float facingSpeedThreshold = hasSettings
            ? CpBaseEnemySettings.FacingSpeedThreshold
            : DefaultFacingSpeedThreshold;
        float massAccelInfluence = hasSettings
            ? CpBaseEnemySettings.MassAccelerationInfluence
            : DefaultMassAccelerationInfluence;
        float massTurnInfluence = hasSettings
            ? CpBaseEnemySettings.MassTurnRateInfluence
            : DefaultMassTurnRateInfluence;
        float referenceMass = hasSettings ? CpBaseEnemySettings.ReferenceMass : DefaultReferenceMass;
        float minMassFactor = hasSettings ? CpBaseEnemySettings.MinMassFactor : DefaultMinMassFactor;

        _steeringLookup.Update(ref state);
        _avoidanceLookup.Update(ref state);
        _finalStatsLookup.Update(ref state);
        _stunLookup.Update(ref state);
        _knockbackLookup.Update(ref state);
        _stopDistanceLookup.Update(ref state);
        _cellBufferLookup.Update(ref state);

        var moveJob = new MoveFlowFieldSnappedJob
        {
            DeltaTime = SystemAPI.Time.DeltaTime,
            PlanetCenter = planetData.Center,
            PlanetRadius = planetData.Radius,
            PhysicsCollisionWorld = collisionWorld,
            FlowField = flowFieldData,
            FlowFieldEntity = flowFieldEntity,
            CellBufferLookup = _cellBufferLookup,
            SteeringLookup = _steeringLookup,
            AvoidanceLookup = _avoidanceLookup,
            FinalStatsLookup = _finalStatsLookup,
            StunLookup = _stunLookup,
            KnockbackLookup = _knockbackLookup,
            StopDistanceLookup = _stopDistanceLookup,
            GlobalAcceleration = globalAcceleration,
            GlobalMaxTurnRateDeg = globalMaxTurnRateDeg,
            FacingSpeedThreshold = facingSpeedThreshold,
            MassAccelerationInfluence = massAccelInfluence,
            MassTurnRateInfluence = massTurnInfluence,
            ReferenceMass = referenceMass,
            MinMassFactor = minMassFactor
        };
        state.Dependency = moveJob.ScheduleParallel(state.Dependency);
    }

    /// <summary>
    /// Samples the flow field to determine movement direction, then snaps the entity to terrain via raycast.
    /// Steering forces from AvoidanceSystem are added on top of the flow field direction.
    /// </summary>
    // FlowFieldFollowerMovement is not listed here: it is taken as a `ref` parameter in Execute (the job
    // writes its Velocity), which already puts it in the query — and being enableable, entities with it
    // disabled are filtered out exactly as before.
    [BurstCompile]
    [WithAll(typeof(HardSnappedMovement))]
    private partial struct MoveFlowFieldSnappedJob : IJobEntity
    {
        [ReadOnly] public float DeltaTime;
        [ReadOnly] public float3 PlanetCenter;
        [ReadOnly] public float PlanetRadius;
        [ReadOnly] public CollisionWorld PhysicsCollisionWorld;
        [ReadOnly] public FlowFieldData FlowField;
        [ReadOnly] public Entity FlowFieldEntity;

        [ReadOnly] public BufferLookup<FlowFieldCell> CellBufferLookup;
        [ReadOnly] public ComponentLookup<SteeringForce> SteeringLookup;
        [ReadOnly] public ComponentLookup<Avoidance> AvoidanceLookup;
        [ReadOnly] public ComponentLookup<FinalStats> FinalStatsLookup;
        [ReadOnly] public ComponentLookup<StunEffect> StunLookup;
        [ReadOnly] public ComponentLookup<ActiveKnockback> KnockbackLookup;
        [ReadOnly] public ComponentLookup<StopDistance> StopDistanceLookup;

        /// <summary> Global movement feel; the per-entity fields on FlowFieldFollowerMovement override it. </summary>
        [ReadOnly] public float GlobalAcceleration;
        [ReadOnly] public float GlobalMaxTurnRateDeg;

        /// <summary> Below this speed the entity faces the player instead of its (meaningless) velocity direction. </summary>
        [ReadOnly] public float FacingSpeedThreshold;

        /// <summary> Exponents controlling how much Avoidance mass damps acceleration / turn rate (0 = ignore mass). </summary>
        [ReadOnly] public float MassAccelerationInfluence;
        [ReadOnly] public float MassTurnRateInfluence;
        [ReadOnly] public float ReferenceMass;

        /// <summary> Floor on the mass factor, so a very heavy entity never becomes unable to move or turn. </summary>
        [ReadOnly] public float MinMassFactor;

        // Half-length of the short ground probe (the entity moves at most a couple of units per frame);
        // SnapDistance is the fallback probe when the ground is out of the short ray's reach.
        private const float GroundProbeDistance = 4f;
        private const float SnapDistance = 500f;
        private const float VertSnapSpeed = 20.0f;
        // Cap on the avoidance steering's share of the movement direction.
        private const float SteeringBlend = 0.35f;
        // Width of the deceleration band just outside the stop ring, as a fraction of StopDistance.
        private const float StopSlowBandFraction = 0.5f;
        // Flow field is trusted while the entity is within this cosine (cos 45°) of the grid normal;
        // an angle, not a distance, so it is independent of planet radius.
        private const float FlowFieldMinCos = 0.7f;

        public void Execute(Entity entity, ref LocalTransform transform, ref FlowFieldFollowerMovement movement)
        {
            // Stunned entities do not move; velocity is cleared so they restart from a standstill.
            if (StunLookup.TryGetComponent(entity, out var _) && StunLookup.IsComponentEnabled(entity))
            {
                movement.Velocity = float3.zero;
                return;
            }

            // Knocked-back entities are driven by KnockbackSystem instead; leave their position alone.
            if (KnockbackLookup.HasComponent(entity) && KnockbackLookup.IsComponentEnabled(entity))
            {
                movement.Velocity = float3.zero;
                return;
            }

            // Per-prefab movement feel, falling back to the global settings when left at 0.
            float acceleration = movement.Acceleration > 0f ? movement.Acceleration : GlobalAcceleration;
            float maxTurnRateDeg = movement.MaxTurnRateDeg > 0f ? movement.MaxTurnRateDeg : GlobalMaxTurnRateDeg;

            // Heavier entities accelerate and turn more slowly. Separate exponents for the two (mass vs
            // moment of inertia); MinMassFactor floors the slowdown so a very heavy entity never freezes.
            bool usesMass = MassAccelerationInfluence > 0f || MassTurnRateInfluence > 0f;
            if (usesMass && AvoidanceLookup.HasComponent(entity))
            {
                float mass = math.max(0.0001f, AvoidanceLookup[entity].Mass);
                float massRatio = ReferenceMass / mass;

                if (MassAccelerationInfluence > 0f)
                    acceleration *= math.max(MinMassFactor,
                        math.pow(massRatio, MassAccelerationInfluence));

                if (MassTurnRateInfluence > 0f)
                    maxTurnRateDeg *= math.max(MinMassFactor,
                        math.pow(massRatio, MassTurnRateInfluence));
            }

            float3 currentNormal = math.normalize(transform.Position - PlanetCenter);

            // Geodesic heading toward the player (great-circle tangent, projected onto the surface).
            // Used as the navigation fallback when the flow field can't be trusted, and as the facing
            // target for a near-stationary entity whose velocity direction is meaningless.
            float3 toGoal = FlowField.Origin - transform.Position;
            PlanetUtils.ProjectDirectionOnSurface(in toGoal, in currentNormal, out float3 goalDirection);
            bool hasGoalDirection = math.lengthsq(goalDirection) > 0.0001f;
            float3 fallbackDirection = hasGoalDirection ? goalDirection : transform.Forward();

            // Flow field near the player, geodesic seek beyond the trusted cone (see FlowFieldMinCos):
            // the grid projects orthographically, so the far hemisphere folds onto the near one.
            float3 flowDirection;
            if (math.dot(currentNormal, FlowField.GridNormal) < FlowFieldMinCos)
            {
                flowDirection = fallbackDirection;
            }
            else
            {
                flowDirection = SampleFlowField(transform.Position);
                flowDirection = math.lengthsq(flowDirection) > 0.001f
                    ? math.normalize(flowDirection)
                    : fallbackDirection; // blocked / unreachable cell: seek instead of drifting forward
            }

            // Avoidance steering, only while Avoidance is enabled: the LOD disables it far from the
            // player and stops updating SteeringForce, whose last value would otherwise stay applied.
            float3 steeringForce = float3.zero;
            if (AvoidanceLookup.HasComponent(entity) && AvoidanceLookup.IsComponentEnabled(entity)
                                                     && SteeringLookup.HasComponent(entity))
                steeringForce = SteeringLookup[entity].Value;

            float3 desiredDirection = flowDirection + steeringForce * SteeringBlend;

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

            // Re-project the combined flow + steering direction onto the local tangent plane (it was
            // built from the player's tangent frame and a raw world-space steering vector).
            PlanetUtils.ProjectDirectionOnSurface(in desiredDirection, in currentNormal,
                out float3 moveDirection);
            if (math.lengthsq(moveDirection) < 0.0001f)
                moveDirection = fallbackDirection;

            // Move speed comes from FinalStats (CoreStats + active slow, folded in by ActiveEffectsSystem).
            float speed = 3f;
            if (FinalStatsLookup.HasComponent(entity))
                speed = FinalStatsLookup[entity].MoveSpeed;

            // Accelerate a persistent velocity toward the desired one rather than snapping to it. This
            // low-passes the tick-to-tick jitter of the avoidance term, keeping both path and facing stable.
            float3 desiredVelocity = moveDirection * (speed * speedFactor);

            // The surface normal rotates as the entity travels, so re-project the carried velocity tangent.
            float3 carried = movement.Velocity - currentNormal * math.dot(movement.Velocity, currentNormal);

            float3 deltaV = desiredVelocity - carried;
            float deltaVLen = math.length(deltaV);
            float maxDeltaV = acceleration * DeltaTime;
            movement.Velocity = deltaVLen > maxDeltaV && deltaVLen > 1e-6f
                ? carried + deltaV * (maxDeltaV / deltaVLen)
                : desiredVelocity;

            float currentSpeed = math.length(movement.Velocity);

            // Face the direction of travel; below the speed threshold face the player instead (a
            // near-zero velocity has no meaningful direction, and a stopped melee enemy should face its target).
            float3 faceDirection = currentSpeed > FacingSpeedThreshold
                ? movement.Velocity / currentSpeed
                : fallbackDirection;

            float3 desiredPosition = transform.Position + movement.Velocity * DeltaTime;

            // Beyond the camera horizon (Avoidance disabled by the LOD), snap to the perfect sphere with
            // no raycast — terrain detail is off-screen there. Entities re-raycast on crossing back in.
            bool beyondHorizon = AvoidanceLookup.HasComponent(entity)
                                 && !AvoidanceLookup.IsComponentEnabled(entity);
            if (beyondHorizon)
            {
                PlanetUtils.SnapToSurfaceRadius(in desiredPosition, in PlanetCenter, PlanetRadius,
                    out float3 snapped);
                transform.Position = snapped;

                PlanetUtils.GetSurfaceNormalRadius(in snapped, in PlanetCenter, out float3 sphereNormal);
                PlanetUtils.GetRotationOnSurface(in faceDirection, sphereNormal, out quaternion sphereRotation);
                transform.Rotation = RotateTowards(transform.Rotation, sphereRotation,
                    math.radians(maxTurnRateDeg) * DeltaTime);
                return;
            }

            // Terrain snap near the player: short probe first, falling back to the long ray only when it
            // misses (cliff edge, spawn, teleport).
            var input = new RaycastInput
            {
                Start = desiredPosition + currentNormal * GroundProbeDistance,
                End = desiredPosition - currentNormal * GroundProbeDistance,
                Filter = new CollisionFilter
                {
                    BelongsTo = CollisionLayers.Raycast,
                    CollidesWith = CollisionLayers.Landscape
                }
            };

            bool grounded = PhysicsCollisionWorld.CastRay(input, out var hit);
            if (!grounded)
            {
                input.Start = desiredPosition + currentNormal * SnapDistance;
                input.End = desiredPosition - currentNormal * SnapDistance;
                grounded = PhysicsCollisionWorld.CastRay(input, out hit);
            }

            if (grounded)
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
                transform.Rotation = RotateTowards(transform.Rotation, targetRotation,
                    math.radians(maxTurnRateDeg) * DeltaTime);
            }
            else
            {
                transform.Position = desiredPosition;

                // Keep turning even when off the navigable mesh.
                PlanetUtils.GetRotationOnSurface(in faceDirection, currentNormal, out quaternion targetRotation);
                transform.Rotation = RotateTowards(transform.Rotation, targetRotation,
                    math.radians(maxTurnRateDeg) * DeltaTime);
            }
        }

        /// <summary>
        /// Rotates <paramref name="from"/> toward <paramref name="to"/> by at most
        /// <paramref name="maxRadians"/> — a hard cap on angular speed (degrees/second).
        /// </summary>
        private static quaternion RotateTowards(quaternion from, quaternion to, float maxRadians)
        {
            // Angle between two unit quaternions; abs() picks the shorter of the two equivalent arcs.
            float dot = math.clamp(math.abs(math.dot(from.value, to.value)), -1f, 1f);
            float angle = 2f * math.acos(dot);

            if (angle <= maxRadians || angle < 1e-5f)
                return to;

            return math.slerp(from, to, maxRadians / angle);
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
