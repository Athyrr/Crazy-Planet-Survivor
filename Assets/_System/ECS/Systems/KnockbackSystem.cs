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
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PhysicsWorldSingleton>();
        state.RequireForUpdate<PlanetData>();
        state.RequireForUpdate<EffectTypeConfig>();
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        var deltaTime = SystemAPI.Time.DeltaTime;

         var planetPos = SystemAPI.GetSingleton<PlanetData>().Center;

        var collisionWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld;
        var effectConfig = SystemAPI.GetSingleton<EffectTypeConfig>().Blob;

        new ProcessKnockbackJob
        {
            DeltaTime = deltaTime,
            PlanetPos = planetPos,
            CollisionWorld = collisionWorld,
            EffectConfig = effectConfig,
            ECB = ecb.AsParallelWriter()
        }.ScheduleParallel();
    }

    [BurstCompile]
    private partial struct ProcessKnockbackJob : IJobEntity
    {
        public float DeltaTime;
        public float3 PlanetPos;
        [ReadOnly] public CollisionWorld CollisionWorld;
        [ReadOnly] public BlobAssetReference<EffectTypeConfigBlob> EffectConfig;
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
            ref KnockbackState knockback,
            ref LiveStats liveStats)
        {
            knockback.RemainingTime -= DeltaTime;

            if (knockback.RemainingTime <= 0)
            {
              ECB.SetComponentEnabled<KnockbackState>(chunkIndex, entity, false);
                return;
            }

            // Knockback resistance scales the whole push; at >= 1 the entity is fully immune, so the
            // effect is disabled outright rather than left as a near-zero drift.
            // Read KBResist straight off the iterated LiveStats (temporary buffs/debuffs are visible) —
            // a ComponentLookup<LiveStats> here would alias the RW chunk handle for the same type.
            float kbResist = liveStats.KBResist;
            if (kbResist >= 1f)
            {
                ECB.SetComponentEnabled<KnockbackState>(chunkIndex, entity, false);
                return;
            }

            // Force follows the designer curve: X = elapsed/duration (0 at impact, 1 at end).
            float elapsedNorm = math.saturate(1f - knockback.RemainingTime / knockback.MaxDuration);
            float currentForce = knockback.InitialForce * (1f - kbResist) * EvaluateCurve(EffectConfig.Value.KnockbackForceCurveSamples, elapsedNorm);

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

        private static float EvaluateCurve(in BlobArray<float> samples, float t)
        {
            int len = samples.Length;
            float x = math.saturate(t) * (len - 1);
            int i0 = (int)x;
            int i1 = math.min(i0 + 1, len - 1);
            float f = x - i0;
            return math.lerp(samples[i0], samples[i1], f);
        }
    }
}