using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>Analytic anchoring of attached children: owner holds the common angle; child = caster + Radius ×
/// dir(Angle + 2π·Index/Count) in the Planet (tangent plane) or Facing frame. No Parent hierarchy.</summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(TransformSystemGroup))]
[UpdateAfter(typeof(EntitiesMovementSystem))]
[UpdateAfter(typeof(FlowFieldMovementSystem))]
[BurstCompile]
public partial struct EmitterAnchorSystem : ISystem
{
    private ComponentLookup<EmitterOwner> _ownerLookup;
    private ComponentLookup<SpellSource> _sourceLookup;
    private ComponentLookup<LocalTransform> _transformLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<EmitterOwner>();
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        _ownerLookup = state.GetComponentLookup<EmitterOwner>(true);
        _sourceLookup = state.GetComponentLookup<SpellSource>(true);
        _transformLookup = state.GetComponentLookup<LocalTransform>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton<GameState>(out var gs) || gs.State != EGameState.Running)
            return;

        float3 planetCenter = SystemAPI.TryGetSingleton<PlanetData>(out var planet) ? planet.Center : float3.zero;
        var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);

        var angleHandle = new OwnerAngleJob { DeltaTime = SystemAPI.Time.DeltaTime }.ScheduleParallel(state.Dependency);

        _ownerLookup.Update(ref state);
        _sourceLookup.Update(ref state);
        _transformLookup.Update(ref state);
        state.Dependency = new AnchorJob
        {
            OwnerLookup = _ownerLookup,
            SourceLookup = _sourceLookup,
            TransformLookup = _transformLookup,
            PlanetCenter = planetCenter,
            ECB = ecb.AsParallelWriter(),
        }.ScheduleParallel(angleHandle);
    }

    [BurstCompile]
    private partial struct OwnerAngleJob : IJobEntity
    {
        public float DeltaTime;

        private void Execute(ref EmitterOwner owner)
        {
            owner.Angle = math.fmod(owner.Angle + owner.AngularSpeed * DeltaTime, 2f * math.PI);
        }
    }

    [BurstCompile]
    private partial struct AnchorJob : IJobEntity
    {
        [ReadOnly] public ComponentLookup<EmitterOwner> OwnerLookup;
        [ReadOnly] public ComponentLookup<SpellSource> SourceLookup;
        // Read (caster) + write by ref (this anchor) of the same type: aliasing patron of CopyEntityPositionSystem.
        [ReadOnly, NativeDisableContainerSafetyRestriction] public ComponentLookup<LocalTransform> TransformLookup;
        public float3 PlanetCenter;
        public EntityCommandBuffer.ParallelWriter ECB;

        private void Execute([ChunkIndexInQuery] int chunkIndex, Entity entity, ref LocalTransform transform, in EmitterAnchor anchor)
        {
            // Orphan anchor (owner destroyed) or owner without a living caster: self-destroy.
            if (!OwnerLookup.TryGetComponent(anchor.Owner, out var owner)
                || !SourceLookup.TryGetComponent(anchor.Owner, out var source)
                || !TransformLookup.TryGetComponent(source.CasterEntity, out var caster))
            {
                ECB.DestroyEntity(chunkIndex, entity);
                return;
            }

            float3 normal = math.normalizesafe(caster.Position - PlanetCenter, new float3(0f, 1f, 0f));
            float3 hint = owner.Frame == EEmitterFrame.Facing ? caster.Forward() : new float3(0f, 0f, 1f);
            float3 reference = EmitterMath.TangentReference(normal, hint);
            float angle = EmitterMath.CircleAngleRadians(owner.Angle, anchor.Index, math.max(1, owner.Count));
            float3 offset = EmitterMath.OrbitOffset(normal, reference, owner.Radius, angle);

            transform.Position = caster.Position + offset;
            transform.Rotation = quaternion.LookRotationSafe(math.cross(normal, math.normalizesafe(offset, reference)), normal);
            transform.Scale = owner.ChildScale;
        }
    }
}
