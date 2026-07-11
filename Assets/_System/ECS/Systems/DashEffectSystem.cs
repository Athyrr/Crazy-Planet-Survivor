using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Extensions;
using Unity.Transforms;

/// <summary>
/// Applies the behavioural add-ons of a dash (see <see cref="DashEffect"/>) while an entity is dashing:
/// knocking back enemies it passes through, and reflecting enemy projectiles back at their casters.
/// Runs only on entities whose <see cref="ActiveDash"/> is currently enabled, right after
/// <see cref="DashSystem"/> has moved them for the frame. Reuses the existing <see cref="ActiveKnockback"/>
/// pipeline for the push; reflection re-targets the projectile's damage and collider filter.
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
        // Direct RW writes replace ECB.AppendToBuffer for DashDamage so a target dying same-frame from
        // this very dash can no longer trigger "Buffer does not exist on entity" during ECB playback.
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

    /// <summary>
    /// Handles the melee-ish, on-contact dash effects for enemies within <see cref="DashEffect.KnockbackRadius"/>:
    /// knockback, one-shot dash damage (deduped per dash via the DashHitEntity buffer), and stamping the
    /// chain-damage marker onto knocked enemies so <see cref="DashChainDamageSystem"/> can propagate it.
    /// </summary>
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

        public void Execute(Entity dasher, in LocalTransform transform, in ActiveDash activeDash,
            in DashEffect effect, DynamicBuffer<DashHitEntity> dashHits)
        {
            bool doKnockback = effect.Knockback;
            bool doDamage = effect.DashDamage > 0f;
            if (!doKnockback && !doDamage)
                return;

            // KnockbackRadius doubles as the dash "contact radius" for both push and damage.
            float radius = effect.KnockbackRadius;
            if (radius <= 0f)
                return;

            float3 origin = transform.Position;
            float3 up = math.normalize(origin - PlanetCenter);
            bool doChain = doKnockback && effect.KnockbackChainDamage > 0f;

            // Push in the direction the dash is going, projected onto the ground. Using the dasher's
            // travel direction (not "away from dasher") gives a stable, Alistar-headbutt-style shove:
            // it never flips when the dasher passes through the target, and it stays consistent even if
            // we run this job multiple frames while the same enemy lingers in range.
            PlanetUtils.ProjectDirectionOnSurface(activeDash.Direction, up, out float3 pushDir);
            if (math.lengthsq(pushDir) < 1e-5f)
                pushDir = transform.Forward();
            pushDir = math.normalize(pushDir);

            var hits = new NativeList<DistanceHit>(16, Allocator.Temp);
            // Enemies collide with Raycast (like the existing bounce target search), so this catches them.
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

                    // One dedup covers self, duplicate collider leaves of the same enemy, AND repeated
                    // frames of the same dash lingering on the target. Both knockback and dash damage
                    // are one-shot: no restamping means the knockback direction/force curve/duration
                    // stay clean and the enemy is damaged exactly once by this dash.
                    if (DashHitContains(dashHits, enemy))
                        continue;
                    dashHits.Add(new DashHitEntity { Value = enemy });

                    if (doKnockback && KnockbackLookup.HasComponent(enemy))
                    {
                        // ActiveKnockback + DashChainDamage are pre-added (disabled) by ActiveEffectsAuthoring,
                        // so we only Set + Enable — no structural change means no crash if the target
                        // dies from the same-frame DashDamage before this ECB replays.
                        ECB.SetComponent(enemy, new ActiveKnockback
                        {
                            Direction = pushDir,
                            InitialForce = effect.KnockbackForce,
                            DurationLeft = KnockbackDuration,
                            MaxDuration = KnockbackDuration,
                        });
                        ECB.SetComponentEnabled<ActiveKnockback>(enemy, true);

                        // (b) Mark the flung enemy so it damages enemies it collides with while airborne.
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

                    // (a) One-shot dash damage (dedup already applied above). Direct RW buffer write —
                    // not ECB.AppendToBuffer — so a same-frame kill can't tear the buffer down before playback.
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

    /// <summary>
    /// Reflects straight enemy projectiles within <see cref="DashEffect.ReflectRadius"/>: reverses their
    /// heading, re-points their damage at enemies, and flips their collider filter so they now hit enemies.
    /// The collider is made unique first (blob cloned + tracked for cleanup) so only this projectile changes.
    /// </summary>
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
            // Enemy projectiles belong to Spell and CollideWith Player, so query as "the player being hit".
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

                    if (!DamageLookup.HasComponent(proj) || !LinearLookup.HasComponent(proj))
                        continue; // only straight (LinearMovement) projectiles are reflectable

                    var dmg = DamageLookup[proj];
                    // Skip anything not currently aimed at the player (already reflected / player's own spell).
                    if ((dmg.TargetLayers & CollisionLayers.Player) == 0)
                        continue;

                    // Reverse heading, optionally speeding the projectile up. A non-positive multiplier
                    // (e.g. the default 0 on prefabs saved before this field existed) means "unchanged".
                    var lm = LinearLookup[proj];
                    lm.Direction = -lm.Direction;
                    if (effect.ReflectSpeedMultiplier > 0f)
                        lm.Speed *= effect.ReflectSpeedMultiplier;
                    ECB.SetComponent(proj, lm);

                    // Re-aim the damage at enemies (optionally boosted).
                    dmg.TargetLayers = CollisionLayers.Enemy | CollisionLayers.Obstacle;
                    dmg.Damage *= math.max(0f, effect.ReflectDamageMultiplier);
                    ECB.SetComponent(proj, dmg);

                    // Flip the physics collider filter so it now triggers on enemies (unique blob → no shared side effects).
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
