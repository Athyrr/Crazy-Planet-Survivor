using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Systems;
using Unity.Transforms;

[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
[UpdateAfter(typeof(PhysicsSystemGroup))]
[BurstCompile]
public partial struct CollisionSystem : ISystem
{
    private ComponentLookup<Player> _playerLookup;
    private ComponentLookup<Destructible> _cpEntityLookup;
    private ComponentLookup<LocalTransform> _transformLookup;
    private ComponentLookup<LocalToWorld> _ltwLookup;

    private ComponentLookup<DamageOnContact> _damageOnContactLookup;
    private ComponentLookup<DestroyOnContact> _destroyOnContactLookup;
    private ComponentLookup<Invincible> _invincibleLookup;
    private ComponentLookup<DashIFrames> _dashIFramesLookup;
    private BufferLookup<HitEntityMemory> _hitMemoryLookup;

    private ComponentLookup<Bounce> _ricochetLookup;
    private ComponentLookup<Pierce> _pierceLookup;
    private ComponentLookup<LinearMovement> _linearMovementLookup;
    private ComponentLookup<FollowTargetMovement> _followMovementLookup;

    // todo clean this, tmp fix
    private ComponentLookup<SlowEffect> _slowLookup;
    private ComponentLookup<StunEffect> _stunLookup;
    private ComponentLookup<BurnEffect> _burnLookup;
    private ComponentLookup<ActiveKnockback> _knockbackLookup;

    private ComponentLookup<ExplodeOnContact> _explodeLookup;
    private ComponentLookup<SpellSource> _subSpellRootLookup;
    private BufferLookup<ActiveSpell> _activeSpellBufferLookup;
    private ComponentLookup<Boss> _bossLookup;

    private NativeQueue<SpellDamageEvent> _damageEventsQueue;

    private ComponentLookup<PhysicsCollider> _colliderLookup;
    private ComponentLookup<Lifetime> _lifetimeLookup;

    // private ActiveEffectsConfig _effectsConfig;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Player>();
        state.RequireForUpdate<ActiveEffectsConfig>();
        state.RequireForUpdate<PhysicsStep>();
        state.RequireForUpdate<SimulationSingleton>();
        state.RequireForUpdate<PhysicsWorldSingleton>();
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();

        _slowLookup = state.GetComponentLookup<SlowEffect>(true);
        _stunLookup = state.GetComponentLookup<StunEffect>(true);
        _burnLookup = state.GetComponentLookup<BurnEffect>(true);
        _knockbackLookup = state.GetComponentLookup<ActiveKnockback>(true);

        _playerLookup = state.GetComponentLookup<Player>(true);
        _cpEntityLookup = state.GetComponentLookup<Destructible>(true);
        _transformLookup = state.GetComponentLookup<LocalTransform>(true);
        _ltwLookup = state.GetComponentLookup<LocalToWorld>(true);

        _damageOnContactLookup = state.GetComponentLookup<DamageOnContact>(true);
        _destroyOnContactLookup = state.GetComponentLookup<DestroyOnContact>(true);
        _invincibleLookup = state.GetComponentLookup<Invincible>(true);
        _dashIFramesLookup = state.GetComponentLookup<DashIFrames>(true);
        _hitMemoryLookup = state.GetBufferLookup<HitEntityMemory>(false);

        _ricochetLookup = state.GetComponentLookup<Bounce>(false);
        _pierceLookup = state.GetComponentLookup<Pierce>(false);
        _linearMovementLookup = state.GetComponentLookup<LinearMovement>(false);
        _followMovementLookup = state.GetComponentLookup<FollowTargetMovement>(false);

        _explodeLookup = state.GetComponentLookup<ExplodeOnContact>(true);
        _subSpellRootLookup = state.GetComponentLookup<SpellSource>(true);
        _activeSpellBufferLookup = state.GetBufferLookup<ActiveSpell>(false);
        _bossLookup = state.GetComponentLookup<Boss>(true);

        _colliderLookup = state.GetComponentLookup<PhysicsCollider>(true);
        _lifetimeLookup = state.GetComponentLookup<Lifetime>(false);

        // _effectsConfig = SystemAPI.GetSingleton<ActiveEffectsConfig>();

        _damageEventsQueue = new NativeQueue<SpellDamageEvent>(Allocator.Persistent);
    }

    public void OnDestroy(ref SystemState state)
    {
        if (_damageEventsQueue.IsCreated)
            _damageEventsQueue.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton<GameState>(out var gameState) || gameState.State != EGameState.Running)
            return;

        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
        var physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>();

        var effectsConfig = SystemAPI.GetSingleton<ActiveEffectsConfig>();
        float lifeStealConversion = SystemAPI.TryGetSingleton<LifeStealConfig>(out var lifeStealCfg)
            ? lifeStealCfg.Conversion
            : 0.075f;

        _slowLookup.Update(ref state);
        _stunLookup.Update(ref state);
        _burnLookup.Update(ref state);
        _playerLookup.Update(ref state);
        _cpEntityLookup.Update(ref state);
        _transformLookup.Update(ref state);
        _ltwLookup.Update(ref state);
        _damageOnContactLookup.Update(ref state);
        _destroyOnContactLookup.Update(ref state);
        _invincibleLookup.Update(ref state);
        _dashIFramesLookup.Update(ref state);
        _hitMemoryLookup.Update(ref state);
        _ricochetLookup.Update(ref state);
        _pierceLookup.Update(ref state);
        _knockbackLookup.Update(ref state);
        _linearMovementLookup.Update(ref state);
        _followMovementLookup.Update(ref state);
        _explodeLookup.Update(ref state);
        _subSpellRootLookup.Update(ref state);
        _activeSpellBufferLookup.Update(ref state);
        _colliderLookup.Update(ref state);
        _bossLookup.Update(ref state);
        _lifetimeLookup.Update(ref state);

        var playerEntity = SystemAPI.GetSingletonEntity<Player>();

        // Second command buffer, dedicated to ResolveHit (a parallel writer): keeps its records off the
        // plain `ecb` used for bounce/pierce/destroy/explosion so the two writer flavors don't mix on one
        // buffer. Both play back at EndSimulation.
        var ecbResolve = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
        var resolveContext = new ResolveHitContext
        {
            EffectsConfig = effectsConfig,
            LifeStealConversion = lifeStealConversion,
            PlayerEntity = playerEntity,
            SlowLookup = _slowLookup,
            StunLookup = _stunLookup,
            BurnLookup = _burnLookup,
            KnockbackLookup = _knockbackLookup,
            LtwLookup = _ltwLookup,
            ActiveSpellLookup = _activeSpellBufferLookup,
            DamageEventsWriter = _damageEventsQueue.AsParallelWriter(),
        };

        var triggerCollisionJob = new TriggerCollisionJob
        {
            Seed = (uint)(SystemAPI.Time.ElapsedTime * 1000) + 1,

            ECB = ecb,
            ResolveECB = ecbResolve.AsParallelWriter(),
            Resolve = resolveContext,
            CurrentTime = SystemAPI.Time.ElapsedTime,
            CollisionWorld = physicsWorld.CollisionWorld,

            PlayerEntity = playerEntity,

            PlayerLookup = _playerLookup,
            DestructibleLookup = _cpEntityLookup,
            DamageOnContactLookup = _damageOnContactLookup,
            DestroyOnContactLookup = _destroyOnContactLookup,
            LocalTransformLookup = _transformLookup,

            BounceLookup = _ricochetLookup,
            PierceLookup = _pierceLookup,
            LinearMovementLookup = _linearMovementLookup,
            FollowMovementLookup = _followMovementLookup,

            HitMemoryLookup = _hitMemoryLookup,
            InvincibleLookup = _invincibleLookup,
            DashIFramesLookup = _dashIFramesLookup,
            ExplodeOnContactLookup = _explodeLookup,

            SpellSourceLookup = _subSpellRootLookup,

            ColliderLookup = _colliderLookup,
            BossLookup = _bossLookup,
            LifetimeLookup = _lifetimeLookup,
        };

        JobHandle triggerHandle =
            triggerCollisionJob.Schedule(SystemAPI.GetSingleton<SimulationSingleton>(), state.Dependency);

        var trackDamageJob = new TrackDamageJob
        {
            DamageEventsQueue = _damageEventsQueue,
            ActiveSpellLookup = _activeSpellBufferLookup,
            PlayerEntity = SystemAPI.GetSingletonEntity<Player>(),
        };

        state.Dependency = trackDamageJob.Schedule(triggerHandle);
    }

    [BurstCompile]
    private struct TriggerCollisionJob : ITriggerEventsJob
    {
        public uint Seed;

        public EntityCommandBuffer ECB;
        // Dedicated parallel writer for ResolveHit (damage/effects/heal/statmod); see OnUpdate.
        public EntityCommandBuffer.ParallelWriter ResolveECB;
        public ResolveHitContext Resolve;
        public double CurrentTime;
        [ReadOnly] public CollisionWorld CollisionWorld;

        public Entity PlayerEntity;

        [ReadOnly] public ComponentLookup<Player> PlayerLookup;
        [ReadOnly] public ComponentLookup<Destructible> DestructibleLookup;

        [ReadOnly] public ComponentLookup<DamageOnContact> DamageOnContactLookup;
        [ReadOnly] public ComponentLookup<DestroyOnContact> DestroyOnContactLookup;
        [ReadOnly] public ComponentLookup<Invincible> InvincibleLookup;
        [ReadOnly] public ComponentLookup<DashIFrames> DashIFramesLookup;

        public BufferLookup<HitEntityMemory> HitMemoryLookup;

        [ReadOnly] public ComponentLookup<LocalTransform> LocalTransformLookup;
        public ComponentLookup<LinearMovement> LinearMovementLookup;
        public ComponentLookup<FollowTargetMovement> FollowMovementLookup;
        public ComponentLookup<Bounce> BounceLookup;
        public ComponentLookup<Pierce> PierceLookup;
        [ReadOnly] public ComponentLookup<ExplodeOnContact> ExplodeOnContactLookup;

        [ReadOnly] public ComponentLookup<SpellSource> SpellSourceLookup;
        [ReadOnly] public ComponentLookup<PhysicsCollider> ColliderLookup;
        [ReadOnly] public ComponentLookup<Boss> BossLookup;
        public ComponentLookup<Lifetime> LifetimeLookup;

        private const double MultiHitDelay = 1f; // Delay before allowing another hit if collision stays.

        public void Execute(TriggerEvent triggerEvent)
        {
            Entity entityA = triggerEvent.EntityA;
            Entity entityB = triggerEvent.EntityB;

            if (TryResolveDamagerVsTarget(entityA, entityB, out Entity damagerEntity, out Entity target))
            {
                var damageData = DamageOnContactLookup[damagerEntity];
                if (damageData.TargetLayers != 0)
                {
                    // uint targetBelongsTo = DestructibleLookup[target].LayerMask;
                    uint targetBelongsTo = CollisionLayers.Everything;
                    if (ColliderLookup.HasComponent(target))
                    {
                        targetBelongsTo = ColliderLookup[target].Value.Value.GetCollisionFilter().BelongsTo;
                    }

                    if ((damageData.TargetLayers & targetBelongsTo) == 0)
                    {
                        return;
                    }
                }

                bool canDealDamage = true;

                if (HitMemoryLookup.HasBuffer(damagerEntity))
                {
                    var history = HitMemoryLookup[damagerEntity];
                    bool asAlreadyHit = false;

                    for (var i = 0; i < history.Length; i++)
                    {
                        if (history[i].HitEntity == target)
                        {
                            asAlreadyHit = true;
                            if (CurrentTime - history[i].LastHitTime < MultiHitDelay)
                            {
                                canDealDamage = false;
                            }
                            else
                            {
                                var hitData = history[i];
                                hitData.LastHitTime = CurrentTime;
                                history[i] = hitData;
                            }

                            break;
                        }
                    }

                    if (!asAlreadyHit)
                        history.Add(new HitEntityMemory { HitEntity = target, LastHitTime = CurrentTime });
                }

                if (canDealDamage)
                {
                    // Dash i-frames: a target mid-dash with invincibility enabled ignores this hit.
                    bool dashInvincible = DashIFramesLookup.HasComponent(target)
                                          && DashIFramesLookup.IsComponentEnabled(target);

                    // An immune target lets the damager pass through untouched: no damage, and (below)
                    // no destroy/explode either — so a dash's reflect can catch enemy projectiles instead
                    // of them being consumed on contact with the invincible player.
                    bool targetImmune = InvincibleLookup.HasComponent(target) || dashInvincible;

                    // todo let target receive damge even if invincible. Consume damage on Health system and avoid health loss instead
                    if (!targetImmune)
                    {
                        var random = Random.CreateFromIndex((Seed ^ ((uint)entityA.Index * 0x9E3779B1u) ^ ((uint)entityB.Index * 0x85EBCA77u)) | 1u);

                        int dbIndex = -1;
                        Entity caster = Entity.Null;
                        if (SpellSourceLookup.TryGetComponent(damagerEntity, out var spellSource))
                        {
                            dbIndex = spellSource.DatabaseIndex;
                            caster = spellSource.CasterEntity;
                        }

                        // The whole crit → damage buffer → tag effects → life steal → tracking bundle now
                        // lives in ResolveHit, shared with the area paths (kills the "crit gruyère").
                        var action = HitAction.MakeDamage(damageData.Damage, damageData.TotalCritChance,
                            damageData.TotalCritMultiplier, damageData.Tags);
                        var hitSource = new HitSource
                        {
                            Caster = caster,
                            DatabaseIndex = dbIndex,
                            PushOrigin = LocalTransformLookup[PlayerEntity].Position,
                            Shake = ResolveShakeSource(damagerEntity, damageData.Tags),
                        };
                        ResolveHit.Apply(in Resolve, ResolveECB, target.Index, target, in action, in hitSource, ref random);

                        // Feedbacks
                        ApplyFeedbacks(target);
                    }

                    if (!dashInvincible &&
                        ExplodeOnContactLookup.TryGetComponent(damagerEntity, out var explosion) &&
                        ExplodeOnContactLookup.IsComponentEnabled(damagerEntity))
                    {
                        var random = Random.CreateFromIndex((Seed ^ ((uint)entityA.Index * 0x9E3779B1u) ^ ((uint)entityB.Index * 0x85EBCA77u)) | 1u);

                        bool isCrit = random.NextFloat(0f, 1f) <= damageData.TotalCritChance;
                        float criticalDamagesMultiplier = 1f;
                        if (isCrit)
                            criticalDamagesMultiplier = math.max(1.0f, damageData.TotalCritMultiplier);

                        CreateExplosion(damagerEntity, damageData.TargetLayers, explosion, criticalDamagesMultiplier,
                            isCrit, ECB);
                    }

                    // Only a dash's i-frames let the projectile pass through unharmed (so the dash's
                    // reflect can catch it). The debug Invincible tag still destroys projectiles on
                    // contact as before — it only cancels damage, not the impact.
                    bool shouldDestroy = !dashInvincible && DestroyOnContactLookup.HasComponent(damagerEntity);

                    if (!dashInvincible && BounceLookup.HasComponent(damagerEntity))
                    {
                        var bounce = BounceLookup[damagerEntity];
                        if (bounce.RemainingBounces > 0)
                        {
                            if (TryFindNextUnvisitedTarget(damagerEntity, target, bounce.BounceRange, out Entity newTarget,
                                    out float3 newDirection))
                            // if (TryFindNextTarget(damagerEntity, target, bounce.BounceRange, out Entity newTarget,
                            //         out float3 newDirection))
                            {
                                if (LinearMovementLookup.IsComponentEnabled(damagerEntity))
                                    ECB.SetComponentEnabled<LinearMovement>(damagerEntity, false);

                                ECB.SetComponentEnabled<FollowTargetMovement>(damagerEntity, true);

                                ECB.SetComponent(damagerEntity, new FollowTargetMovement
                                {
                                    Target = newTarget,
                                    Speed = math.max(1, bounce.BounceSpeed)
                                });

                                bounce.RemainingBounces--;
                                BounceLookup[damagerEntity] = bounce;
                                shouldDestroy = false;
                            }
                            else
                            {
                                shouldDestroy = true;
                            }
                        }
                        else
                        {
                            shouldDestroy = true;
                        }
                    }

                    else if (!dashInvincible && PierceLookup.HasComponent(damagerEntity))
                    {
                        var pierce = PierceLookup[damagerEntity];
                        if (pierce.RemainingPierces > 0)
                        {
                            pierce.RemainingPierces--;
                            ECB.SetComponent(damagerEntity, pierce);
                            shouldDestroy = false;
                        }
                        else
                        {
                            shouldDestroy = true;
                        }
                    }

                    // A bounce/pierce spell that survives a hit gets its lifetime refreshed, so
                    // chained hits keep it alive instead of letting it expire mid-flight.
                    if (!shouldDestroy
                        && (BounceLookup.HasComponent(damagerEntity) || PierceLookup.HasComponent(damagerEntity))
                        && LifetimeLookup.HasComponent(damagerEntity))
                    {
                        var lifetime = LifetimeLookup[damagerEntity];
                        lifetime.TimeLeft = lifetime.Duration;
                        LifetimeLookup[damagerEntity] = lifetime;
                    }

                    if (shouldDestroy && DestructibleLookup.HasComponent(damagerEntity))
                    {
                        ECB.SetComponentEnabled<DestroyEntityFlag>(damagerEntity, true);
                    }
                }
            }
        }

        private void CreateExplosion(Entity damager, uint targetLayers, ExplodeOnContact explosionData,
            float criticalDamagesMultiplier,
            bool isCrit, EntityCommandBuffer ECB)
        {
            var requestEntity = ECB.CreateEntity();

            ESpellTag tags = ESpellTag.None;
            if (DamageOnContactLookup.HasComponent(damager))
            {
                tags = DamageOnContactLookup[damager].Tags | ESpellTag.Explosive;
            }

            int dbIndex = -1;
            if (SpellSourceLookup.TryGetComponent(damager, out var spellSource))
            {
                dbIndex = spellSource.DatabaseIndex;
            }

            float3 pos = LocalTransformLookup[damager].Position;

            ECB.AddComponent(
                requestEntity,
                new ExplosionRequest()
                {
                    Position = pos,
                    Damage = explosionData.Damage * criticalDamagesMultiplier,
                    VfxPrefab = explosionData.VfxPrefab,
                    IsCritical = isCrit,
                    Tags = tags,
                    // TargetLayers = collisionLayer,
                    TargetLayers = targetLayers,
                    DatabaseIndex = dbIndex,
                    Damager = damager
                }
            );
        }

        private void ApplyFeedbacks(Entity hitEntity)
        {
            if (!DestructibleLookup.HasComponent(hitEntity))
                return;

            // var shakeReq = ECB.CreateEntity(0);
            // ECB.AddComponent<ShakeFeedbackRequest>(0, shakeReq);

            // todo request on the entity itself
            // var flashReq = ECB.CreateEntity(0);
            // ECB.AddComponent(0, flashReq, new HitFrameColorRequest { TargetEntity = hitEntity });
        }

        private bool TryResolveDamagerVsTarget(Entity entityA, Entity entityB, out Entity damager, out Entity target)
        {
            if (
                DamageOnContactLookup.HasComponent(entityA)
                && (DestructibleLookup.HasComponent(entityB))
            )
            {
                damager = entityA;
                target = entityB;
                return true;
            }

            if (
                DamageOnContactLookup.HasComponent(entityB)
                && (DestructibleLookup.HasComponent(entityA))
            )
            {
                damager = entityB;
                target = entityA;
                return true;
            }

            damager = Entity.Null;
            target = Entity.Null;
            return false;
        }

        /// <summary>
        /// Maps a damager to a camera-shake category. Explosions win (Explosive tag); otherwise the
        /// attacker rank is read from the damager directly (contact) or, for a projectile/spell,
        /// from its <see cref="SpellSource.CasterEntity"/>.
        /// </summary>
        private EDamageShakeSource ResolveShakeSource(Entity damager, ESpellTag tags)
        {
            if ((tags & ESpellTag.Explosive) != 0)
                return EDamageShakeSource.Explosion;

            Entity attacker = damager;
            if (SpellSourceLookup.TryGetComponent(damager, out var spellSource)
                && spellSource.CasterEntity != Entity.Null)
                attacker = spellSource.CasterEntity;

            if (BossLookup.TryGetComponent(attacker, out var boss))
                return boss.Kind == EBossKind.FinalBoss
                    ? EDamageShakeSource.Boss
                    : EDamageShakeSource.Elite;

            return EDamageShakeSource.Enemy;
        }

        // todo try resolve player collide with enemies/damaging obstacles

        private bool TryFindNextTarget(Entity projectile, Entity currentTarget, float range, out Entity newTarget,
            out float3 direction)
        {
            // var memory = HitMemoryLookup[projectile];
            float3 currentPos = LocalTransformLookup[projectile].Position;
            var hits = new NativeList<DistanceHit>(16, Allocator.Temp);
            var filter = new CollisionFilter
            {
                BelongsTo = CollisionLayers.Raycast,
                CollidesWith = CollisionLayers.Enemy
            };

            CollisionWorld.OverlapSphere(currentPos, range, ref hits, filter);

            float closestDistSq = float.MaxValue;
            float3 bestPos = float3.zero;
            bool found = false;
            newTarget = Entity.Null;

            for (int i = 0; i < hits.Length; i++)
            {
                var hit = hits[i];
                if (hit.Entity == currentTarget || hit.Entity == projectile)
                    continue;

                if (!DestructibleLookup.HasComponent(hit.Entity))
                    continue;

                if (hit.Distance < closestDistSq)
                {
                    closestDistSq = hit.Distance;
                    bestPos = hit.Position;
                    newTarget = hit.Entity;
                    found = true;
                }
            }

            direction = found ? math.normalize(bestPos - currentPos) : float3.zero;

            hits.Dispose();
            return found;
        }

        private bool TryFindNextUnvisitedTarget(Entity projectile, Entity currentTarget, float range,
            out Entity newTarget,
            out float3 direction)
        {
            float3 currentPos = LocalTransformLookup[projectile].Position;
            var hits = new NativeList<DistanceHit>(16, Allocator.Temp);

            var filter = new CollisionFilter
            {
                BelongsTo = CollisionLayers.Raycast,
                CollidesWith = CollisionLayers.Enemy
            };

            CollisionWorld.OverlapSphere(currentPos, range, ref hits, filter);

            bool hasMemory = HitMemoryLookup.HasBuffer(projectile);
            DynamicBuffer<HitEntityMemory> memory = default;
            if (hasMemory)
            {
                memory = HitMemoryLookup[projectile];
            }

            float closestUnvisitedDistSq = float.MaxValue;
            float closestVisitedDistSq = float.MaxValue;

            Entity bestUnvisited = Entity.Null;
            Entity bestVisited = Entity.Null;

            float3 bestUnvisitedPos = float3.zero;
            float3 bestVisitedPos = float3.zero;

            for (int i = 0; i < hits.Length; i++)
            {
                var hit = hits[i];

                if (hit.Entity == currentTarget || hit.Entity == projectile)
                    continue;

                if (!DestructibleLookup.HasComponent(hit.Entity))
                    continue;

                bool alreadyVisited = false;
                if (hasMemory)
                {
                    for (int m = 0; m < memory.Length; m++)
                    {
                        if (memory[m].HitEntity == hit.Entity)
                        {
                            alreadyVisited = true;
                            break;
                        }
                    }
                }

                if (alreadyVisited)
                {
                    if (hit.Distance < closestVisitedDistSq)
                    {
                        closestVisitedDistSq = hit.Distance;
                        bestVisitedPos = hit.Position;
                        bestVisited = hit.Entity;
                    }
                }
                else
                {
                    if (hit.Distance < closestUnvisitedDistSq)
                    {
                        closestUnvisitedDistSq = hit.Distance;
                        bestUnvisitedPos = hit.Position;
                        bestUnvisited = hit.Entity;
                    }
                }
            }

            hits.Dispose();

            if (bestUnvisited != Entity.Null)
            {
                newTarget = bestUnvisited;
                direction = math.normalize(bestUnvisitedPos - currentPos);
                return true;
            }

            if (bestVisited != Entity.Null)
            {
                newTarget = bestVisited;
                direction = math.normalize(bestVisitedPos - currentPos);
                return true;
            }

            newTarget = Entity.Null;
            direction = float3.zero;
            return false;
        }
    }

    [BurstCompile]
    private struct TrackDamageJob : IJob
    {
        public NativeQueue<SpellDamageEvent> DamageEventsQueue;
        public BufferLookup<ActiveSpell> ActiveSpellLookup;
        public Entity PlayerEntity;

        public void Execute()
        {
            // Sums damage per spell map
            var sums = new NativeHashMap<int, int>(16, Allocator.Temp);

            while (DamageEventsQueue.TryDequeue(out var evt))
            {
                if (sums.ContainsKey(evt.DatabaseIndex))
                    sums[evt.DatabaseIndex] += evt.DamageAmount;
                else
                    sums.Add(evt.DatabaseIndex, evt.DamageAmount);
            }

            if (ActiveSpellLookup.TryGetBuffer(PlayerEntity, out var buffer))
            {
                for (int i = 0; i < buffer.Length; i++)
                {
                    var spell = buffer[i];
                    if (sums.TryGetValue(spell.DatabaseIndex, out int totalAdded))
                    {
                        spell.TotalDamageDealt += totalAdded;
                        buffer[i] = spell;
                    }
                }
            }

            sums.Dispose();
        }
    }
}

public struct SpellDamageEvent
{
    public int DatabaseIndex;
    public int DamageAmount;
}