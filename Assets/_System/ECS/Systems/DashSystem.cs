using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

/// <summary>
/// Drives the dash for any entity carrying <see cref="DashSettings"/>. A dash is a non-physical,
/// pre-resolved motion: when triggered (<see cref="DashRequest"/> enabled) the landing point is
/// computed once — walking the geodesic and stopping in front of the first obstacle so the entity
/// can never tunnel through walls — then the transform is tweened from start to end along the baked
/// motion curve. Charge count / recharge time come from <see cref="CoreStats"/> so they are upgradeable.
/// Runs before <see cref="EntitiesMovementSystem"/> and zeroes MoveSpeed while dashing so the input
/// movement never fights the tween (same approach as <see cref="KnockbackSystem"/>).
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(ActiveEffectsSystem))]
[UpdateBefore(typeof(EntitiesMovementSystem))]
[BurstCompile]
public partial struct DashSystem : ISystem
{
    private ComponentLookup<LinearMovement> _linearLookup;
    private ComponentLookup<StunEffect> _stunLookup;
    private ComponentLookup<ActiveDash> _activeDashLookup;
    private BufferLookup<DashHitEntity> _dashHitLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PlanetData>();
        state.RequireForUpdate<PhysicsWorldSingleton>();

        _linearLookup = state.GetComponentLookup<LinearMovement>(true);
        _stunLookup = state.GetComponentLookup<StunEffect>(true);
        _activeDashLookup = state.GetComponentLookup<ActiveDash>(false);
        _dashHitLookup = state.GetBufferLookup<DashHitEntity>(false);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton<GameState>(out var gameState))
            return;
        if (gameState.State != EGameState.Running && gameState.State != EGameState.Lobby)
            return;

        _linearLookup.Update(ref state);
        _stunLookup.Update(ref state);
        _activeDashLookup.Update(ref state);
        _dashHitLookup.Update(ref state);

        var planetData = SystemAPI.GetSingleton<PlanetData>();
        var collisionWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld;

        state.Dependency = new DashJob
        {
            DeltaTime = SystemAPI.Time.DeltaTime,
            PlanetCenter = planetData.Center,
            CollisionWorld = collisionWorld,
            LinearLookup = _linearLookup,
            StunLookup = _stunLookup,
            ActiveDashLookup = _activeDashLookup,
            DashHitLookup = _dashHitLookup,
        }.Schedule(state.Dependency);
    }

    [BurstCompile]
    [WithPresent(typeof(DashRequest), typeof(DashIFrames))]
    private partial struct DashJob : IJobEntity
    {
        [ReadOnly] public float DeltaTime;
        [ReadOnly] public float3 PlanetCenter;
        [ReadOnly] public CollisionWorld CollisionWorld;

        [ReadOnly] public ComponentLookup<LinearMovement> LinearLookup;
        [ReadOnly] public ComponentLookup<StunEffect> StunLookup;

        // ActiveDash (data AND enabled state) is accessed only through this lookup — never as a query
        // ref/in or EnabledRefRW param. A query param would force filtering on ActiveDash being *enabled*
        // (excluding resting dashers, so a dash could never start), and an EnabledRefRW would alias this
        // lookup. Each entity touches only its own ActiveDash, so parallel writes are safe.
        [NativeDisableParallelForRestriction] public ComponentLookup<ActiveDash> ActiveDashLookup;

        // Per-dash hit dedup buffer lives on the dasher; cleared here when a new dash starts.
        [NativeDisableParallelForRestriction] public BufferLookup<DashHitEntity> DashHitLookup;

        private const float SnapDistance = 500f;
        private const float RotSmoothSpeed = 20f;

        public void Execute(
            Entity entity,
            ref LocalTransform transform,
            ref DashState dashState,
            ref FinalStats finalStats,
            in DashSettings settings,
            in CoreStats coreStats,
            EnabledRefRW<DashRequest> requestEnabled,
            EnabledRefRW<DashIFrames> iframesEnabled)
        {
            ActiveDash activeDash = ActiveDashLookup[entity];
            bool isDashing = ActiveDashLookup.IsComponentEnabled(entity);
            int maxCharges = coreStats.DashCount;

            // First tick: fill charges to the (upgrade-resolved) ceiling so we start full.
            if (dashState.ChargesAvailable < 0)
            {
                dashState.ChargesAvailable = maxCharges;
                dashState.RechargeTimers.Clear();
            }

            // ---- Phase A: recharge (independent per-charge timers) + global cooldown ----

            // Reconcile capacity with DashCount (upgrades can raise/lower it at runtime).
            // Invariant target: ChargesAvailable + RechargeTimers.Length == maxCharges.
            int total = dashState.ChargesAvailable + dashState.RechargeTimers.Length;
            if (total < maxCharges)
            {
                // New capacity is granted immediately (a fresh charge, ready to use).
                dashState.ChargesAvailable += maxCharges - total;
            }
            else if (total > maxCharges)
            {
                int excess = total - maxCharges;
                int fromAvail = math.min(excess, dashState.ChargesAvailable);
                dashState.ChargesAvailable -= fromAvail;
                excess -= fromAvail;
                while (excess > 0 && dashState.RechargeTimers.Length > 0)
                {
                    dashState.RechargeTimers.RemoveAt(dashState.RechargeTimers.Length - 1);
                    excess--;
                }
            }

            if (dashState.GlobalCooldownTimer > 0f)
                dashState.GlobalCooldownTimer -= DeltaTime;

            // Each spent charge counts down on its own; whichever reaches zero refunds a charge.
            for (int i = dashState.RechargeTimers.Length - 1; i >= 0; i--)
            {
                float remaining = dashState.RechargeTimers[i] - DeltaTime;
                if (remaining <= 0f)
                {
                    dashState.RechargeTimers.RemoveAt(i);
                    if (dashState.ChargesAvailable < maxCharges)
                        dashState.ChargesAvailable++;
                }
                else
                {
                    dashState.RechargeTimers[i] = remaining;
                }
            }

            // ---- Phase C: a dash is already in flight ----
            if (isDashing)
            {
                requestEnabled.ValueRW = false; // ignore re-triggers mid-dash

                activeDash.Elapsed += DeltaTime;
                float t = math.saturate(activeDash.Elapsed / activeDash.Duration);
                float progress = EvaluateCurve(settings.Curve, t);

                float3 pos = math.lerp(activeDash.StartPos, activeDash.EndPos, progress);
                if (PlanetUtils.SnapToSurfaceRaycast(ref CollisionWorld, pos, PlanetCenter,
                        LandscapeFilter(), SnapDistance, out RaycastHit hit))
                    pos = hit.Position;

                transform.Position = pos;

                float3 normal = math.normalize(transform.Position - PlanetCenter);
                PlanetUtils.ProjectDirectionOnSurface(activeDash.Direction, normal, out float3 tangent);
                if (math.lengthsq(tangent) > 1e-5f)
                {
                    PlanetUtils.GetRotationOnSurface(tangent, normal, out quaternion targetRot);
                    transform.Rotation = math.slerp(transform.Rotation, targetRot, DeltaTime * RotSmoothSpeed);
                }

                // Neutralize input-driven movement for this frame (EntitiesMovementSystem runs after).
                finalStats.MoveSpeed = 0f;

                if (t >= 1f)
                {
                    ActiveDashLookup.SetComponentEnabled(entity, false);
                    if (settings.IFrames)
                        iframesEnabled.ValueRW = false;
                }

                ActiveDashLookup[entity] = activeDash;
                return;
            }

            // ---- Phase B: handle a new trigger ----
            if (!requestEnabled.ValueRO)
                return;

            requestEnabled.ValueRW = false; // consume the request

            bool stunned = StunLookup.HasComponent(entity) && StunLookup.IsComponentEnabled(entity);
            if (stunned || dashState.ChargesAvailable <= 0 || dashState.GlobalCooldownTimer > 0f)
                return;

            // Resolve dash direction: current movement intent, else facing.
            float3 rawDir = transform.Forward();
            if (LinearLookup.HasComponent(entity))
            {
                float3 moveDir = LinearLookup[entity].Direction;
                if (math.lengthsq(moveDir) > 1e-4f)
                    rawDir = moveDir;
            }

            float3 startNormal = math.normalize(transform.Position - PlanetCenter);
            PlanetUtils.ProjectDirectionOnSurface(rawDir, startNormal, out float3 dashDir);
            if (math.lengthsq(dashDir) < 1e-5f)
                return; // no valid direction
            dashDir = math.normalize(dashDir);

            float reached = ResolveDashDistance(transform.Position, dashDir, settings, out float3 endPos);

            // Degenerate case (flush against a wall): don't waste a charge on a ~0 dash.
            float minDash = math.min(0.5f, settings.Distance * 0.1f);
            if (reached < minDash)
                return;

            dashState.ChargesAvailable--;
            // Start an independent recharge countdown for the charge just spent.
            float cd = coreStats.DashCooldown;
            if (cd > 0f && dashState.RechargeTimers.Length < dashState.RechargeTimers.Capacity)
                dashState.RechargeTimers.Add(cd);
            dashState.GlobalCooldownTimer = settings.GlobalCooldown;

            // Reset the per-dash hit dedup so DashDamage can hit each enemy once this dash.
            if (DashHitLookup.HasBuffer(entity))
                DashHitLookup[entity].Clear();

            activeDash.StartPos = transform.Position;
            activeDash.EndPos = endPos;
            activeDash.Direction = dashDir;
            activeDash.Elapsed = 0f;
            activeDash.Duration = settings.Duration;
            ActiveDashLookup[entity] = activeDash;
            ActiveDashLookup.SetComponentEnabled(entity, true);

            if (settings.IFrames)
                iframesEnabled.ValueRW = true;
        }

        /// <summary>
        /// Walks the dash path in <see cref="DashSettings.PathSampleCount"/> steps along the surface and
        /// returns the furthest distance reachable before the collider radius overlaps an obstacle.
        /// Because every sample is surface-snapped and tested with the entity's radius, the entity can
        /// neither tunnel through a wall nor squeeze through a gap narrower than itself, and the returned
        /// end point is guaranteed to be a free zone.
        /// </summary>
        private float ResolveDashDistance(float3 start, float3 dir, in DashSettings settings, out float3 endPos)
        {
            int n = settings.PathSampleCount;
            float step = settings.Distance / n;
            float r = settings.Radius;

            var hits = new NativeList<DistanceHit>(8, Allocator.Temp);
            var obstacleFilter = ObstacleFilter();

            float reached = 0f;
            float3 endLocal = start;

            for (int i = 1; i <= n; i++)
            {
                float d = step * i;
                float3 p = start + dir * d;
                if (PlanetUtils.SnapToSurfaceRaycast(ref CollisionWorld, p, PlanetCenter,
                        LandscapeFilter(), SnapDistance, out RaycastHit snap))
                    p = snap.Position;

                hits.Clear();
                CollisionWorld.OverlapSphere(p, r, ref hits, obstacleFilter);
                if (hits.Length > 0)
                    break; // wall ahead: keep the last free sample

                reached = d;
                endLocal = p;
            }

            hits.Dispose();
            endPos = endLocal;
            return reached;
        }

        private static float EvaluateCurve(BlobAssetReference<DashCurveBlob> curve, float t)
        {
            if (!curve.IsCreated)
                return t; // linear fallback

            ref BlobArray<float> samples = ref curve.Value.Samples;
            int len = samples.Length;
            if (len <= 1)
                return t;

            float x = math.saturate(t) * (len - 1);
            int i0 = (int)x;
            int i1 = math.min(i0 + 1, len - 1);
            float f = x - i0;
            return math.lerp(samples[i0], samples[i1], f);
        }

        private static CollisionFilter ObstacleFilter() => new CollisionFilter
        {
            BelongsTo = CollisionLayers.Raycast,
            CollidesWith = CollisionLayers.Obstacle,
        };

        private static CollisionFilter LandscapeFilter() => new CollisionFilter
        {
            BelongsTo = CollisionLayers.Raycast,
            CollidesWith = CollisionLayers.Landscape,
        };
    }
}
