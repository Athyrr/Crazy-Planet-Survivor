using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

/// <summary>
/// Applies environmental hazard-zone effects (lava burn,slow zones, …).
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(ActiveEffectsSystem))]
[BurstCompile]
public partial struct HazardZoneSystem : ISystem
{
    private ComponentLookup<LocalTransform> _transformLookup;
    private ComponentLookup<DestroyEntityFlag> _destroyFlagLookup;
    private BufferLookup<StatusEffectApplyRequest> _requestLookup;
    private BufferLookup<DamageBufferElement> _damageBufferLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PhysicsWorldSingleton>();
        state.RequireForUpdate<HazardZone>();
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        state.RequireForUpdate<EffectTypeConfig>();

        _transformLookup = state.GetComponentLookup<LocalTransform>(true);
        _destroyFlagLookup = state.GetComponentLookup<DestroyEntityFlag>(true);
        _requestLookup = state.GetBufferLookup<StatusEffectApplyRequest>();
        _damageBufferLookup = state.GetBufferLookup<DamageBufferElement>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton<GameState>(out var gameState) || gameState.State != EGameState.Running)
            return;

        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
        var collisionWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld;
        var effectConfig = SystemAPI.GetSingleton<EffectTypeConfig>().Blob;

        _transformLookup.Update(ref state);
        _destroyFlagLookup.Update(ref state);
        _requestLookup.Update(ref state);
        _damageBufferLookup.Update(ref state);

        // NOTE: do NOT read the player's LocalTransform here. Reading it on the main thread would
        // force-complete every job that writes LocalTransform (the movement systems) → a sync stall.
        // The player's position is read inside the job via TransformLookup, where the dependency is
        // resolved in the job graph instead of blocking the main thread.
        Entity playerEntity = SystemAPI.HasSingleton<Player>()
            ? SystemAPI.GetSingletonEntity<Player>()
            : Entity.Null;

        var job = new HazardZoneJob
        {
            ECB = ecb.AsParallelWriter(),
            CollisionWorld = collisionWorld,
            TransformLookup = _transformLookup,
            DestroyFlagLookup = _destroyFlagLookup,
            RequestLookup = _requestLookup,
            DamageBufferLookup = _damageBufferLookup,
            EffectConfig = effectConfig,
            PlayerEntity = playerEntity,
            DeltaTime = SystemAPI.Time.DeltaTime,
        };

        state.Dependency = job.Schedule(state.Dependency);
    }

    [BurstCompile]
    private partial struct HazardZoneJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter ECB;
        [ReadOnly] public CollisionWorld CollisionWorld;
        [ReadOnly] public ComponentLookup<LocalTransform> TransformLookup;
        [ReadOnly] public ComponentLookup<DestroyEntityFlag> DestroyFlagLookup;
        public BufferLookup<StatusEffectApplyRequest> RequestLookup;
        [ReadOnly] public BufferLookup<DamageBufferElement> DamageBufferLookup;
        [ReadOnly] public BlobAssetReference<EffectTypeConfigBlob> EffectConfig;
        public Entity PlayerEntity;
        public float DeltaTime;

        private void Execute([ChunkIndexInQuery] int chunkIndex, Entity zoneEntity,
            ref HazardZone zone, in LocalToWorld zoneTransform,
            in DynamicBuffer<HazardZoneEffectElement> effects)
        {
            // Throttle: refresh effects only every RefreshInterval seconds (per-zone, staggered phase),
            // instead of every frame. Effects keep ticking via ActiveEffects/TickDamage; the zone only
            // tops up their Linger. Requires each effect's Linger >= RefreshInterval (see authoring warning).
            zone.RefreshTimer -= DeltaTime;
            if (zone.RefreshTimer > 0f)
                return;
            zone.RefreshTimer += zone.RefreshInterval > 0f ? zone.RefreshInterval : 0.25f;

            if (effects.Length == 0 || zone.TargetLayers == 0)
                return;

            float3 zonePos = zoneTransform.Position;

            float queryRadius = zone.Shape == EHazardShape.Sphere
                ? zone.Radius
                : math.length(zone.BoxHalfExtents);

            if (queryRadius <= 0f)
                return;

            var filter = new CollisionFilter
            {
                BelongsTo = CollisionLayers.Raycast,
                CollidesWith = zone.TargetLayers,
            };

            var hits = new NativeList<DistanceHit>(32, Allocator.Temp);
            CollisionWorld.OverlapSphere(zonePos, queryRadius, ref hits, filter);

            for (int i = 0; i < hits.Length; i++)
            {
                Entity target = hits[i].Entity;
                if (target == zoneEntity || target == Entity.Null)
                    continue;

                // The player is handled separately via its singleton (see below) — skip it here in case
                // the overlap returns it too, so it is never processed twice.
                if (target == PlayerEntity)
                    continue;

                // Only damageable, still-alive entities.
                if (!DamageBufferLookup.HasBuffer(target))
                    continue;
                if (DestroyFlagLookup.HasComponent(target) && DestroyFlagLookup.IsComponentEnabled(target))
                    continue;
                if (!TransformLookup.HasComponent(target))
                    continue;

                float3 targetPos = TransformLookup[target].Position;
                if (!IsInside(zone, zonePos, targetPos))
                    continue;

                for (int e = 0; e < effects.Length; e++)
                    ApplyEffect(chunkIndex, zoneEntity, target, effects[e]);
            }

            hits.Dispose();

            if (PlayerEntity != Entity.Null
                && (zone.TargetLayers & CollisionLayers.Player) != 0
                && DamageBufferLookup.HasBuffer(PlayerEntity)
                && TransformLookup.HasComponent(PlayerEntity)
                && !(DestroyFlagLookup.HasComponent(PlayerEntity) && DestroyFlagLookup.IsComponentEnabled(PlayerEntity))
                && IsInside(zone, zonePos, TransformLookup[PlayerEntity].Position))
            {
                for (int e = 0; e < effects.Length; e++)
                    ApplyEffect(chunkIndex, zoneEntity, PlayerEntity, effects[e]);
            }
        }

        private static bool IsInside(in HazardZone zone, float3 zonePos, float3 targetPos)
        {
            if (zone.Shape == EHazardShape.Sphere)
                return math.distancesq(zonePos, targetPos) <= zone.Radius * zone.Radius;

            float3 d = math.abs(targetPos - zonePos);
            return d.x <= zone.BoxHalfExtents.x
                   && d.y <= zone.BoxHalfExtents.y
                   && d.z <= zone.BoxHalfExtents.z;
        }

        private void ApplyEffect(int chunkIndex, Entity zoneEntity, Entity target, in HazardZoneEffectElement effect)
        {
            switch (effect.Type)
            {
                case EHazardEffectType.Burn:
                    ApplyBurn(zoneEntity, target, effect);
                    break;

                // todo Slow
                default:
                    break;
            }
        }

        /// <summary>
        /// Enqueues a <see cref="StatusEffectApplyRequest"/> for the target's Burn, sourced from this zone
        /// entity — a spell's Burn and this zone's Burn are distinct <see cref="StatusEffectInstance"/>
        /// entries on the same target (Source differs), drained/refreshed by
        /// <c>ActiveEffectsSystem.DrainApplyRequestsJob</c>.
        /// </summary>
        private void ApplyBurn(Entity zoneEntity, Entity target, in HazardZoneEffectElement effect)
        {
            if (!RequestLookup.HasBuffer(target))
                return;

            ref var entries = ref EffectConfig.Value.Entries;
            ref readonly var cfg = ref EffectTypeConfigLookup.Get(ref entries, EffectType.Burn);

            var requests = RequestLookup[target];
            requests.Add(new StatusEffectApplyRequest
            {
                Type = EffectType.Burn,
                Source = zoneEntity,
                Magnitude = effect.Magnitude,
                Duration = effect.Linger,
                Direction = default,
                StackMode = cfg.StackMode,
                MaxStacks = cfg.MaxStacks,
            });
        }
    }
}
