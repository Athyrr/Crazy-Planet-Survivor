using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Extensions;
using Unity.Transforms;

/// <summary>
/// Applies dash effects (knockback + projectile reflect) while <see cref="ActiveDash"/> is enabled.
/// </summary>  
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(DashSystem))]
[UpdateBefore(typeof(EntitiesMovementSystem))]
[BurstCompile]
public partial struct DashEffectSystem : ISystem
{
    private ComponentLookup<ActiveKnockback> _knockbackLookup;
    private ComponentLookup<DashChainDamage> _chainLookup;
    private ComponentLookup<DamageOnContact> _damageLookup;
    private ComponentLookup<LinearMovement> _linearLookup;
    private ComponentLookup<PhysicsCollider> _colliderLookup;
    private BufferLookup<DamageBufferElement> _damageBufferLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PlanetData>();
        state.RequireForUpdate<PhysicsWorldSingleton>();
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();

        _knockbackLookup = state.GetComponentLookup<ActiveKnockback>(true);
        _chainLookup = state.GetComponentLookup<DashChainDamage>(true);
        _damageLookup = state.GetComponentLookup<DamageOnContact>(false);
        _linearLookup = state.GetComponentLookup<LinearMovement>(false);
        _colliderLookup = state.GetComponentLookup<PhysicsCollider>(false);
        _damageBufferLookup = state.GetBufferLookup<DamageBufferElement>(false);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton<GameState>(out var gameState) || gameState.State != EGameState.Running)
            return;

        _knockbackLookup.Update(ref state);
        _chainLookup.Update(ref state);
        _damageLookup.Update(ref state);
        _linearLookup.Update(ref state);
        _colliderLookup.Update(ref state);
        _damageBufferLookup.Update(ref state);

        float knockbackDuration = SystemAPI.TryGetSingleton<ActiveEffectsConfig>(out var fx)
            ? fx.KnockbackDuration
            : 0.4f;

        float3 planetCenter = SystemAPI.GetSingleton<PlanetData>().Center;
        var collisionWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld;

        var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
            .CreateCommandBuffer(state.WorldUnmanaged);

        var knockbackHandle = new DashContactJob
        {
            PlanetCenter = planetCenter,
            CollisionWorld = collisionWorld,
            KnockbackLookup = _knockbackLookup,
            ChainLookup = _chainLookup,
            DamageBufferLookup = _damageBufferLookup,
            KnockbackDuration = knockbackDuration,
            ECB = ecb,
        }.Schedule(state.Dependency);

        state.Dependency = new DashReflectJob
        {
            CollisionWorld = collisionWorld,
            DamageLookup = _damageLookup,
            LinearLookup = _linearLookup,
            ColliderLookup = _colliderLookup,
            ECB = ecb,
        }.Schedule(knockbackHandle);
    }

    [BurstCompile]
    [WithAll(typeof(ActiveDash))]
    private partial struct DashContactJob : IJobEntity
    {
        [ReadOnly] public float3 PlanetCenter;
        [ReadOnly] public CollisionWorld CollisionWorld;
        [ReadOnly] public ComponentLookup<ActiveKnockback> KnockbackLookup;
        [ReadOnly] public ComponentLookup<DashChainDamage> ChainLookup;
        [NativeDisableParallelForRestriction] public BufferLookup<DamageBufferElement> DamageBufferLookup;
        [ReadOnly] public float KnockbackDuration;
        public EntityCommandBuffer ECB;

        private void Execute(Entity dasher, in LocalTransform transform, in ActiveDash activeDash,
            in DashEffect effect, DynamicBuffer<DashHitEntity> dashHits)
        {
            bool doKnockback = effect.Knockback;
            bool doDamage = effect.DashDamage > 0f;
            if (!doKnockback && !doDamage)
                return;

            // KnockbackRadius doubles as the damage contact radius.
            float radius = effect.KnockbackRadius;
            if (radius <= 0f)
                return;

            float3 origin = transform.Position;
            float3 up = math.normalize(origin - PlanetCenter);
            bool doChain = doKnockback && effect.KnockbackChainDamage > 0f;

            // Fallback direction when an enemy sits exactly on the dasher (radial = 0).
            PlanetUtils.ProjectDirectionOnSurface(activeDash.Direction, up, out float3 fallbackDir);
            if (math.lengthsq(fallbackDir) < 1e-5f)
                fallbackDir = transform.Forward();
            fallbackDir = math.normalize(fallbackDir);

            var hits = new NativeList<DistanceHit>(16, Allocator.Temp);
            var filter = new CollisionFilter
            {
                BelongsTo = CollisionLayers.Raycast,
                CollidesWith = CollisionLayers.Enemy,
            };

            if (CollisionWorld.OverlapSphere(origin, radius, ref hits, filter))
            {
                for (int i = 0; i < hits.Length; i++)
                {
                    Entity enemy = hits[i].Entity;
                    if (enemy == dasher)
                        continue;

                    // Per-dash dedup: covers duplicate collider leaves + multiple frames of the same dash.
                    if (DashHitContains(dashHits, enemy))
                        continue;
                    dashHits.Add(new DashHitEntity { Value = enemy });

                    if (doKnockback && KnockbackLookup.HasComponent(enemy))
                    {
                        // Radial push: each enemy is repelled away from the dasher, projected on surface.
                        float3 radial = hits[i].Position - origin;
                        PlanetUtils.ProjectDirectionOnSurface(radial, up, out float3 pushDir);
                        pushDir = math.lengthsq(pushDir) > 1e-5f ? math.normalize(pushDir) : fallbackDir;

                        // ActiveKnockback + DashChainDamage are pre-added disabled: Set + Enable only,
                        // no structural change so a same-frame kill can't break ECB playback.
                        ECB.SetComponent(enemy, new ActiveKnockback
                        {
                            Direction = pushDir,
                            InitialForce = effect.KnockbackForce,
                            DurationLeft = KnockbackDuration,
                            MaxDuration = KnockbackDuration,
                        });
                        ECB.SetComponentEnabled<ActiveKnockback>(enemy, true);

                        // Chain: flung enemy damages others it collides with while airborne.
                        if (doChain && ChainLookup.HasComponent(enemy))
                        {
                            ECB.SetComponent(enemy, new DashChainDamage
                            {
                                Damage = effect.KnockbackChainDamage,
                                Radius = effect.KnockbackChainRadius,
                                TickTimer = 0f,
                            });
                            ECB.SetComponentEnabled<DashChainDamage>(enemy, true);
                        }
                    }

                    // Direct RW write — a same-frame kill can't tear the buffer down before playback.
                    if (doDamage && DamageBufferLookup.HasBuffer(enemy))
                    {
                        DamageBufferLookup[enemy].Add(new DamageBufferElement
                        {
                            Damage = (int)effect.DashDamage,
                            Tag = ESpellTag.None,
                            IsCritical = false,
                            ShakeSource = EDamageShakeSource.None,
                        });
                    }
                }
            }

            hits.Dispose();
        }

        private static bool DashHitContains(in DynamicBuffer<DashHitEntity> buffer, Entity e)
        {
            for (int i = 0; i < buffer.Length; i++)
                if (buffer[i].Value == e)
                    return true;
            return false;
        }
    }

    /// <summary>Reflects straight enemy projectiles: flips heading + damage target + collider filter.</summary>
    [BurstCompile]
    [WithAll(typeof(ActiveDash))]
    private partial struct DashReflectJob : IJobEntity
    {
        [ReadOnly] public CollisionWorld CollisionWorld;

        [ReadOnly] public ComponentLookup<DamageOnContact> DamageLookup;
        [ReadOnly] public ComponentLookup<LinearMovement> LinearLookup;
        [ReadOnly] public ComponentLookup<PhysicsCollider> ColliderLookup;

        public EntityCommandBuffer ECB;

        public void Execute(Entity dasher, in LocalTransform transform, in DashEffect effect)
        {
            if (!effect.Reflect || effect.ReflectRadius <= 0f)
                return;

            var hits = new NativeList<DistanceHit>(16, Allocator.Temp);
            // Enemy projectiles are Spell hitting Player, so we query as "the player being hit".
            var filter = new CollisionFilter
            {
                BelongsTo = CollisionLayers.Player,
                CollidesWith = CollisionLayers.Spell,
            };

            if (CollisionWorld.OverlapSphere(transform.Position, effect.ReflectRadius, ref hits, filter))
            {
                var processed = new NativeHashSet<Entity>(hits.Length, Allocator.Temp);
                for (int i = 0; i < hits.Length; i++)
                {
                    Entity proj = hits[i].Entity;
                    if (proj == dasher || !processed.Add(proj))
                        continue;

                    // Only straight (LinearMovement) projectiles are reflectable.
                    if (!DamageLookup.HasComponent(proj) || !LinearLookup.HasComponent(proj))
                        continue;

                    var dmg = DamageLookup[proj];
                    // Skip anything not aimed at the player (already reflected / player's own spell).
                    if ((dmg.TargetLayers & CollisionLayers.Player) == 0)
                        continue;

                    // Reverse heading; non-positive multiplier means unchanged (legacy prefab default).
                    var lm = LinearLookup[proj];
                    lm.Direction = -lm.Direction;
                    if (effect.ReflectSpeedMultiplier > 0f)
                        lm.Speed *= effect.ReflectSpeedMultiplier;
                    ECB.SetComponent(proj, lm);

                    dmg.TargetLayers = CollisionLayers.Enemy | CollisionLayers.Obstacle;
                    dmg.Damage *= math.max(0f, effect.ReflectDamageMultiplier);
                    ECB.SetComponent(proj, dmg);

                    // Unique blob before mutating the filter, otherwise every sibling projectile flips too.
                    if (ColliderLookup.HasComponent(proj))
                    {
                        var col = ColliderLookup[proj];
                        if (col.IsValid)
                        {
                            col.MakeUnique(proj, ECB);
                            var f = col.Value.Value.GetCollisionFilter();
                            f.CollidesWith = CollisionLayers.Enemy | CollisionLayers.Obstacle;
                            col.Value.Value.SetCollisionFilter(f);
                            ECB.SetComponent(proj, col);
                        }
                    }
                }

                processed.Dispose();
            }

            hits.Dispose();
        }
    }
}
