using _System.Settings;
using Unity.Collections;
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
// Runs every frame (like EntitiesMovementSystem) so flow-field followers move smoothly at the render
// rate instead of stepping at the ~66 Hz CustomUpdateGroup tick. It reads the flow-field grid and the
// avoidance SteeringForce, both still computed at 66 Hz in CustomUpdateGroup — sampling their latest
// values each frame is fine and much smoother.
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

    // Not Burst-compiled: reads the managed CpBaseEnemySettings asset so the movement feel (acceleration,
    // turn rate, mass influence) is tunable live in the inspector. The heavy per-entity work stays in the
    // Burst job below, which receives plain floats.
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

        // Half-length of the ground probe. The entity is already standing on the surface and advances
        // at most (speed * dt) per frame, so the ground under its next position is a couple of units
        // away at most -- a +-4 ray covers it. SnapDistance stays as the fallback for the rare frames
        // where the ground really is out of reach (cliff edge, spawn, end of a knockback, teleport).
        private const float GroundProbeDistance = 4f;
        private const float SnapDistance = 500f;
        private const float VertSnapSpeed = 20.0f;
        // Avoidance is already in world-space units; cap its contribution so it
        // doesn't overpower the flow-field direction on its own.
        private const float SteeringBlend = 0.35f;
        // Just outside the stop ring the entity decelerates over a band of
        // (StopDistance * StopSlowBandFraction) units so it eases in ("adapts its speed")
        // instead of slamming to a halt.
        private const float StopSlowBandFraction = 0.5f;
        // Angular gate for trusting the flow field, as cos(45 deg) against the grid normal. Expressed
        // as an angle rather than a distance so it is independent of the planet radius: the same
        // 45 deg of arc is ~39 units on Earth (R=50) and ~118 on Volcanus (R=150).
        private const float FlowFieldMinCos = 0.7f;

        public void Execute(Entity entity, ref LocalTransform transform, ref FlowFieldFollowerMovement movement)
        {
            // Stunned entities do not move. Velocity is cleared so they do not resume their previous
            // heading the instant the stun ends — they accelerate back up from a standstill.
            if (StunLookup.TryGetComponent(entity, out var _) && StunLookup.IsComponentEnabled(entity))
            {
                movement.Velocity = float3.zero;
                return;
            }

            // Knocked-back entities yield to the KnockbackSystem (which drives their position); otherwise
            // the flow field would overwrite the push every frame and knockback would have no visible effect.
            if (KnockbackLookup.HasComponent(entity) && KnockbackLookup.IsComponentEnabled(entity))
            {
                movement.Velocity = float3.zero;
                return;
            }

            // Per-prefab movement feel, falling back to the global settings when left at 0.
            float acceleration = movement.Acceleration > 0f ? movement.Acceleration : GlobalAcceleration;
            float maxTurnRateDeg = movement.MaxTurnRateDeg > 0f ? movement.MaxTurnRateDeg : GlobalMaxTurnRateDeg;

            // Heavier entities take longer to get going AND to turn (Reynolds divides steering force by
            // mass; the exponents let that be dialled down, since raw inverse-mass is brutal once masses
            // span 1..200). Acceleration and turn rate get separate exponents because they are physically
            // distinct — mass vs moment of inertia.
            // Mass is read regardless of Avoidance's enabled state: it is a property, not transient state.
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

            // --- Direction straight to the player (goal), projected onto the surface. ---
            // Removing the radial component of the straight-line chord leaves exactly the tangent to
            // the great circle joining both points, i.e. the geodesic heading — the shortest path on
            // the sphere. Serves two purposes: the navigation fallback whenever the flow field cannot
            // be trusted, and the facing target for a (near-)stationary entity, whose own velocity
            // direction is meaningless.
            float3 toGoal = FlowField.Origin - transform.Position;
            PlanetUtils.ProjectDirectionOnSurface(in toGoal, in currentNormal, out float3 goalDirection);
            bool hasGoalDirection = math.lengthsq(goalDirection) > 0.0001f;
            // Degenerate only at the exact antipode (chord colinear with the normal), where every
            // direction is equivalent anyway.
            float3 fallbackDirection = hasGoalDirection ? goalDirection : transform.Forward();

            // --- Navigation: flow field near the player, geodesic seek further out. ---
            // The grid is an ORTHOGRAPHIC projection onto the tangent plane at the player, so a point
            // at angle t lands at R*sin(t): past 90 deg the far hemisphere folds back onto the near
            // one and the entity reads someone else's cell. A bounds test never catches this (every
            // point of the sphere projects inside the grid), so the gate has to be angular.
            // Losing the flow field out there only means losing obstacle routing — which is fine,
            // entities re-enter the trusted zone long before reaching the player, and that is exactly
            // where routing is visible and matters.
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

            // --- Blend avoidance steering (capped so it never fully overrides flow) ---
            // Gated on Avoidance being ENABLED: AvoidanceSystem's LOD disables it past its activation
            // radius and then stops writing SteeringForce entirely. SteeringForce is not an enableable
            // component, so without this gate its last computed value stays applied forever — a
            // permanent sideways drift on every entity the player outruns.
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

            // Movement follows the flow field + avoidance so entities path in and keep spacing, but
            // re-projected onto the LOCAL tangent plane first. The flow direction lives in the tangent
            // plane at the PLAYER and the steering force is a raw world vector, so neither is tangent
            // here: applied as-is they push partly into (or out of) the ground and the actual surface
            // speed silently drops by cos(angle) the further the entity is from the player.
            PlanetUtils.ProjectDirectionOnSurface(in desiredDirection, in currentNormal,
                out float3 moveDirection);
            if (math.lengthsq(moveDirection) < 0.0001f)
                moveDirection = fallbackDirection;

            // --- Speed ---
            // FinalStats is the single source of truth: ActiveEffectsSystem folds CoreStats and the
            // active SlowEffect into it every frame. Recomputing it from CoreStats here (as this job
            // used to) silently dropped every slow applied to enemies.
            float speed = 3f;
            if (FinalStatsLookup.HasComponent(entity))
                speed = FinalStatsLookup[entity].MoveSpeed;

            // --- Velocity: accelerate toward the desired velocity instead of snapping to it. ---
            // This is what makes the motion readable. The avoidance term reverses sign tick to tick in a
            // packed crowd; feeding it straight into the position made the heading jitter, which is why
            // the facing used to be driven from the player direction instead. Ramping a stored velocity
            // low-passes that noise, so the path AND the facing derived from it are both stable.
            float3 desiredVelocity = moveDirection * (speed * speedFactor);

            // Re-project the carried velocity: the surface normal rotates as the entity travels around
            // the planet, so last frame's tangent vector is no longer tangent here.
            float3 carried = movement.Velocity - currentNormal * math.dot(movement.Velocity, currentNormal);

            float3 deltaV = desiredVelocity - carried;
            float deltaVLen = math.length(deltaV);
            float maxDeltaV = acceleration * DeltaTime;
            movement.Velocity = deltaVLen > maxDeltaV && deltaVLen > 1e-6f
                ? carried + deltaV * (maxDeltaV / deltaVLen)
                : desiredVelocity;

            float currentSpeed = math.length(movement.Velocity);

            // --- Orientation: face where we are actually going. ---
            // Below the threshold the velocity direction is meaningless (a near-zero vector points
            // nowhere in particular) and driving rotation from it is exactly what spins a blocked entity
            // on the spot. So a stopped entity looks at the player instead — which is also what a melee
            // enemy at attack range should do, so its wind-up stays readable.
            float3 faceDirection = currentSpeed > FacingSpeedThreshold
                ? movement.Velocity / currentSpeed
                : fallbackDirection;

            float3 desiredPosition = transform.Position + movement.Velocity * DeltaTime;

            // --- Visibility LOD: beyond the camera horizon, snap to the perfect sphere with no raycast. ---
            // AvoidanceSystem's LOD disables Avoidance past the horizon (curvature hides the surface), so
            // that same flag also means "too far to see terrain detail". There, a raycast per enemy per
            // frame is pure waste: the analytic sphere snap is a few instructions and any float of terrain
            // is off-screen anyway. Entities re-raycast the instant they cross back inside the horizon.
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

            // --- Raycast terrain snap (near the player) ---
            // Short probe first: a +-4 ray walks a small fraction of the physics BVH compared to the
            // +-500 one, and it hits on virtually every frame. The long ray is only paid on the rare
            // frames the probe misses, so a miss costs one cheap extra cast instead of being the norm.
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
        /// <paramref name="maxRadians"/>. A hard cap on angular speed, unlike the exponential
        /// slerp-by-dt this replaces: that one had no bound, so a large heading change snapped round
        /// almost instantly. Capping in degrees/second is what makes a heavy enemy read as heavy.
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
