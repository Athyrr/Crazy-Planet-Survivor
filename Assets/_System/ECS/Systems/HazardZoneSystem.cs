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
    private ComponentLookup<BurnEffect> _burnLookup;
    private BufferLookup<DamageBufferElement> _damageBufferLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PhysicsWorldSingleton>();
        state.RequireForUpdate<HazardZone>();
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();

        _transformLookup = state.GetComponentLookup<LocalTransform>(true);
        _destroyFlagLookup = state.GetComponentLookup<DestroyEntityFlag>(true);
        _burnLookup = state.GetComponentLookup<BurnEffect>(true);
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

        _transformLookup.Update(ref state);
        _destroyFlagLookup.Update(ref state);
        _burnLookup.Update(ref state);
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
            BurnLookup = _burnLookup,
            DamageBufferLookup = _damageBufferLookup,
            PlayerEntity = playerEntity,
            DeltaTime = SystemAPI.Time.DeltaTime,
        };

        state.Dependency = job.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    private partial struct HazardZoneJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter ECB;
        [ReadOnly] public CollisionWorld CollisionWorld;
        [ReadOnly] public ComponentLookup<LocalTransform> TransformLookup;
        [ReadOnly] public ComponentLookup<DestroyEntityFlag> DestroyFlagLookup;
        [ReadOnly] public ComponentLookup<BurnEffect> BurnLookup;
        [ReadOnly] public BufferLookup<DamageBufferElement> DamageBufferLookup;
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
                    ApplyEffect(chunkIndex, target, effects[e]);
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
                    ApplyEffect(chunkIndex, PlayerEntity, effects[e]);
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

        private void ApplyEffect(int chunkIndex, Entity target, in HazardZoneEffectElement effect)
        {
            switch (effect.Type)
            {
                case EHazardEffectType.Burn:
                    ApplyBurn(chunkIndex, target, effect);
                    break;

                // todo Slow
                default:
                    break;
            }
        }

        /// <summary>
        /// Applies or refreshes the reusable <see cref="BurnEffect"/>.
        /// </summary>
        private void ApplyBurn(int chunkIndex, Entity target, in HazardZoneEffectElement effect)
        {
            if (BurnLookup.HasComponent(target))
            {
                if (BurnLookup.IsComponentEnabled(target))
                {
                    var burn = BurnLookup[target];
                    burn.RemainingTime = math.max(burn.RemainingTime, effect.Linger);
                    burn.DamageOnTick = math.max(burn.DamageOnTick, effect.Magnitude);
                    if (burn.TickRate <= 0f)
                        burn.TickRate = math.max(0.01f, effect.TickRate);
                    ECB.SetComponent(chunkIndex, target, burn);
                }
                else
                {
                    ECB.SetComponent(chunkIndex, target, NewBurn(effect));
                    ECB.SetComponentEnabled<BurnEffect>(chunkIndex, target, true);
                }
            }
            else
            {
                // Entities without ActiveEffectsAuthoring: add it (enabled by default).
                ECB.AddComponent(chunkIndex, target, NewBurn(effect));
            }
        }

        private static BurnEffect NewBurn(in HazardZoneEffectElement effect) => new BurnEffect
        {
            DamageOnTick = effect.Magnitude,
            TickRate = math.max(0.01f, effect.TickRate),
            TickTimer = 0f,
            RemainingTime = effect.Linger,
        };
    }
}
