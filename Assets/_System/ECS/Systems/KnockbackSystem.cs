using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

// Runs AFTER ActiveEffectsSystem (which composes LiveStats) so this system's MoveSpeed=0 override
// is not overwritten again this frame, and BEFORE EntitiesMovementSystem which consumes MoveSpeed.
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(ActiveEffectsSystem))]
[UpdateBefore(typeof(EntitiesMovementSystem))]
[BurstCompile]
public partial struct KnockbackSystem : ISystem
{
    private ComponentLookup<LiveStats> _liveStatsLookup;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PhysicsWorldSingleton>();
        state.RequireForUpdate<PlanetData>();
        state.RequireForUpdate<ActiveEffectsConfig>();
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();

        _liveStatsLookup = state.GetComponentLookup<LiveStats>(isReadOnly: true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        var deltaTime = SystemAPI.Time.DeltaTime;

         var planetPos = SystemAPI.GetSingleton<PlanetData>().Center;

        var collisionWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld;
        var forceCurve = SystemAPI.GetSingleton<ActiveEffectsConfig>().KnockbackForceCurve;

        _liveStatsLookup.Update(ref state);

        new ProcessKnockbackJob
        {
            DeltaTime = deltaTime,
            PlanetPos = planetPos,
            CollisionWorld = collisionWorld,
            ForceCurve = forceCurve,
            LiveStatsLookup = _liveStatsLookup,
            ECB = ecb.AsParallelWriter()
        }.ScheduleParallel();
    }

    [BurstCompile]
    private partial struct ProcessKnockbackJob : IJobEntity
    {
        public float DeltaTime;
        public float3 PlanetPos;
        [ReadOnly] public CollisionWorld CollisionWorld;
        [ReadOnly] public BlobAssetReference<KnockbackCurveBlob> ForceCurve;
        [ReadOnly] public ComponentLookup<LiveStats> LiveStatsLookup;
        public EntityCommandBuffer.ParallelWriter ECB;

        // Vertical band the ground raycast probes above/below the desired position when re-snapping.
        // Same value used by MoveFollowSnappedJob so uneven terrain and cliff edges behave identically.
        private const float SnapDistance = 500f;

        // Short ground probe tried first; the long SnapDistance ray is the fallback (knockback can push
        // an entity off a ledge, out of the short ray's reach).
        private const float GroundProbeDistance = 4f;

        public void Execute(
            [ChunkIndexInQuery] int chunkIndex,
            Entity entity,
            ref LocalTransform transform,
            ref ActiveKnockback knockback,
            ref LiveStats liveStats)
        {
            knockback.DurationLeft -= DeltaTime;

            if (knockback.DurationLeft <= 0)
            {
              ECB.SetComponentEnabled<ActiveKnockback>(chunkIndex, entity, false);
                return;
            }

            // Knockback resistance scales the whole push; at >= 1 the entity is fully immune, so the
            // effect is disabled outright rather than left as a near-zero drift.
            // Read from LiveStats so temporary KBResist buffs/debuffs are visible.
            float kbResist = LiveStatsLookup.HasComponent(entity)
                ? LiveStatsLookup[entity].KBResist
                : 0f;
            if (kbResist >= 1f)
            {
                ECB.SetComponentEnabled<ActiveKnockback>(chunkIndex, entity, false);
                return;
            }

            // Force follows the designer curve: X = elapsed/duration (0 at impact, 1 at end).
            float elapsedNorm = math.saturate(1f - knockback.DurationLeft / knockback.MaxDuration);
            float currentForce = knockback.InitialForce * (1f - kbResist) * EvaluateCurve(ForceCurve, elapsedNorm);

            // Project on ground
            float3 upDir = math.normalize(transform.Position - PlanetPos);
            PlanetUtils.ProjectDirectionOnSurface(knockback.Direction, upDir, out float3 flatDirection);
            // float3 flatDirection = knockback.Direction - math.dot(knockback.Direction, upDir) * upDir;
            if (math.lengthsq(flatDirection) > 0.0001f)
                flatDirection = math.normalize(flatDirection);
            else
                flatDirection = math.normalize(knockback.Direction);

            float3 desiredPos = transform.Position + (flatDirection * currentForce * DeltaTime);

            // Re-snap to the ground each step, otherwise the entity flies tangent to the curved planet
            // and sinks under the surface until the movement systems re-snap it after the knockback ends.
            var snapInput = new RaycastInput
            {
                Start = desiredPos + upDir * GroundProbeDistance,
                End = desiredPos - upDir * GroundProbeDistance,
                Filter = new CollisionFilter
                {
                    BelongsTo = CollisionLayers.Raycast,
                    CollidesWith = CollisionLayers.Landscape,
                },
            };

            bool snapped = CollisionWorld.CastRay(snapInput, out var snapHit);
            if (!snapped)
            {
                snapInput.Start = desiredPos + upDir * SnapDistance;
                snapInput.End = desiredPos - upDir * SnapDistance;
                snapped = CollisionWorld.CastRay(snapInput, out snapHit);
            }

            if (snapped)
                transform.Position = snapHit.Position;
            else
                transform.Position = desiredPos;

            // Set speed to 0 to prevent movement (safe because this system runs after
            // ActiveEffectsSystem — LiveStats has been composed for this frame).
            liveStats.MoveSpeed = 0;
        }

        private static float EvaluateCurve(BlobAssetReference<KnockbackCurveBlob> curve, float t)
        {
            ref BlobArray<float> samples = ref curve.Value.Samples;
            int len = samples.Length;

            float x = math.saturate(t) * (len - 1);
            int i0 = (int)x;
            int i1 = math.min(i0 + 1, len - 1);
            float f = x - i0;
            return math.lerp(samples[i0], samples[i1], f);
        }
    }
}