using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(EntitiesMovementSystem))]
[BurstCompile]
public partial struct KnockbackSystem : ISystem
{
    private ComponentLookup<CoreStats> _coreStatsLookup;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PhysicsWorldSingleton>();
        state.RequireForUpdate<PlanetData>();
        state.RequireForUpdate<ActiveEffectsConfig>();
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();

        _coreStatsLookup = state.GetComponentLookup<CoreStats>(isReadOnly: true);
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

        _coreStatsLookup.Update(ref state);

        new ProcessKnockbackJob
        {
            DeltaTime = deltaTime,
            PlanetPos = planetPos,
            CollisionWorld = collisionWorld,
            ForceCurve = forceCurve,
            CoreStatsLookup = _coreStatsLookup,
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
        [ReadOnly] public ComponentLookup<CoreStats> CoreStatsLookup;
        public EntityCommandBuffer.ParallelWriter ECB;

        // Vertical band the ground raycast probes above/below the desired position when re-snapping.
        // Same value used by MoveFollowSnappedJob so uneven terrain and cliff edges behave identically.
        private const float SnapDistance = 500f;

        // Short probe tried first (see FlowFieldMovementSystem.GroundProbeDistance): the entity is on
        // the surface and a knockback step is small, so the ground is within a few units. Knockback
        // deliberately pushes entities off ledges though, hence the long-ray fallback below.
        private const float GroundProbeDistance = 4f;

        public void Execute(
            [ChunkIndexInQuery] int chunkIndex,
            Entity entity,
            ref LocalTransform transform,
            ref ActiveKnockback knockback,
            ref FinalStats finalStats)
        {
            knockback.DurationLeft -= DeltaTime;

            if (knockback.DurationLeft <= 0)
            {
              ECB.SetComponentEnabled<ActiveKnockback>(chunkIndex, entity, false);
                return;
            }

            // Knockback resistance (Brotato-style flat stat on the defender): scales the whole push.
            // Fully immune at >= 1 -> disable the effect outright so it costs nothing and reads as a
            // hard immunity rather than a near-zero drift. Fixed per archetype, independent of size.
            float kbResist = CoreStatsLookup.HasComponent(entity)
                ? CoreStatsLookup[entity].KnockbackResistance
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

            // Re-snap to the ground: without this the enemy flies tangent to the (curved) planet and
            // sinks under the surface until the flow-field/follow systems re-snap it after the KB ends,
            // which reads as a "clip → pop" at the end. Ray-down from the current up axis to catch the
            // terrain, then take the hit position — same pattern as MoveFollowSnappedJob.
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

            // Set speed to 0 to prevent movement
            finalStats.MoveSpeed = 0;
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