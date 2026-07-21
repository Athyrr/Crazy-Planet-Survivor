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
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PhysicsWorldSingleton>();
        state.RequireForUpdate<PlanetData>();
        state.RequireForUpdate<ActiveEffectsConfig>();
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
        var forceCurve = SystemAPI.GetSingleton<ActiveEffectsConfig>().KnockbackForceCurve;

        new ProcessKnockbackJob
        {
            DeltaTime = deltaTime,
            PlanetPos = planetPos,
            CollisionWorld = collisionWorld,
            ForceCurve = forceCurve,
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
        public EntityCommandBuffer.ParallelWriter ECB;

        // Vertical band the ground raycast probes above/below the desired position when re-snapping.
        // Same value used by MoveFollowSnappedJob so uneven terrain and cliff edges behave identically.
        private const float SnapDistance = 500f;

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

            // Force follows the designer curve: X = elapsed/duration (0 at impact, 1 at end).
            float elapsedNorm = math.saturate(1f - knockback.DurationLeft / knockback.MaxDuration);
            float currentForce = knockback.InitialForce * EvaluateCurve(ForceCurve, elapsedNorm);

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
                Start = desiredPos + upDir * SnapDistance,
                End = desiredPos - upDir * SnapDistance,
                Filter = new CollisionFilter
                {
                    BelongsTo = CollisionLayers.Raycast,
                    CollidesWith = CollisionLayers.Landscape,
                },
            };
            if (CollisionWorld.CastRay(snapInput, out var snapHit))
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