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
    private ComponentLookup<Team> _teamLookup;
    private ComponentLookup<Destructible> _cpEntityLookup;
    private ComponentLookup<LocalTransform> _transformLookup;
    private ComponentLookup<LocalToWorld> _ltwLookup;

    private ComponentLookup<DamageOnContact> _damageOnContactLookup;
    private ComponentLookup<DestroyOnContact> _destroyOnContactLookup;
    private ComponentLookup<Invincible> _invincibleLookup;
    private ComponentLookup<DashIFrames> _dashIFramesLookup;
    private ComponentLookup<GlobalIFrames> _globalIFramesLookup;
    private BufferLookup<HitEntityMemory> _hitMemoryLookup;

    private ComponentLookup<Bounce> _ricochetLookup;
    private ComponentLookup<Pierce> _pierceLookup;
    private ComponentLookup<LinearMovement> _linearMovementLookup;
    private ComponentLookup<FollowTargetMovement> _followMovementLookup;

    private ComponentLookup<CoreStats> _coreStatsLookup;

    private ComponentLookup<SpellSource> _subSpellRootLookup;
    private BufferLookup<ActiveSpell> _activeSpellBufferLookup;
    private ComponentLookup<Summon> _summonLookup;
    private ComponentLookup<Boss> _bossLookup;
    private BufferLookup<StatusEffectApplyRequest> _statusEffectRequestLookup;
    private ComponentLookup<SpellLineage> _spellLineageLookup;
    private ComponentLookup<EmittedHitRules> _emittedHitRulesLookup;
    private ComponentLookup<ReHitDelay> _reHitDelayLookup;
    private ComponentLookup<LiveSpellStats> _liveSpellStatsLookup;

    private NativeQueue<SpellDamageEvent> _damageEventsQueue;

    private ComponentLookup<PhysicsCollider> _colliderLookup;
    private ComponentLookup<Lifetime> _lifetimeLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Player>();
        state.RequireForUpdate<EffectTypeConfig>();
        state.RequireForUpdate<PhysicsStep>();
        state.RequireForUpdate<SimulationSingleton>();
        state.RequireForUpdate<PhysicsWorldSingleton>();
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();

        _coreStatsLookup = state.GetComponentLookup<CoreStats>(true);

        _playerLookup = state.GetComponentLookup<Player>(true);
        _teamLookup = state.GetComponentLookup<Team>(true);
        _cpEntityLookup = state.GetComponentLookup<Destructible>(true);
        _transformLookup = state.GetComponentLookup<LocalTransform>(true);
        _ltwLookup = state.GetComponentLookup<LocalToWorld>(true);

        _damageOnContactLookup = state.GetComponentLookup<DamageOnContact>(true);
        _destroyOnContactLookup = state.GetComponentLookup<DestroyOnContact>(true);
        _invincibleLookup = state.GetComponentLookup<Invincible>(true);
        _dashIFramesLookup = state.GetComponentLookup<DashIFrames>(true);
        // I1 fix: write-enabled (not ReadOnly) so the job can enable GlobalIFrames live, the same frame,
        // the moment the first hit resolves on the player — closes the same-frame multi-enemy gap (the
        // deferred ECB-only enable below plays back at EndSimulation, too late for N simultaneous contacts
        // in this same sequential pass to see it). Safe: this job is .Schedule()-only, never
        // .ScheduleParallel() (see Global Constraints — a live enabled-bit write via ComponentLookup needs
        // single-threaded scheduling).
        _globalIFramesLookup = state.GetComponentLookup<GlobalIFrames>(false);
        _hitMemoryLookup = state.GetBufferLookup<HitEntityMemory>(false);

        _ricochetLookup = state.GetComponentLookup<Bounce>(false);
        _pierceLookup = state.GetComponentLookup<Pierce>(false);
        _linearMovementLookup = state.GetComponentLookup<LinearMovement>(false);
        _followMovementLookup = state.GetComponentLookup<FollowTargetMovement>(false);

        _subSpellRootLookup = state.GetComponentLookup<SpellSource>(true);
        _activeSpellBufferLookup = state.GetBufferLookup<ActiveSpell>(false);
        _summonLookup = state.GetComponentLookup<Summon>(true);
        _bossLookup = state.GetComponentLookup<Boss>(true);
        _statusEffectRequestLookup = state.GetBufferLookup<StatusEffectApplyRequest>(true);
        _spellLineageLookup = state.GetComponentLookup<SpellLineage>(true);
        _emittedHitRulesLookup = state.GetComponentLookup<EmittedHitRules>(true);
        _reHitDelayLookup = state.GetComponentLookup<ReHitDelay>(true);
        _liveSpellStatsLookup = state.GetComponentLookup<LiveSpellStats>(true);

        _colliderLookup = state.GetComponentLookup<PhysicsCollider>(true);
        _lifetimeLookup = state.GetComponentLookup<Lifetime>(false);

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

        float lifeStealConversion = SystemAPI.TryGetSingleton<LifeStealConfig>(out var lifeStealCfg)
            ? lifeStealCfg.Conversion
            : 0.075f;

        _playerLookup.Update(ref state);
        _teamLookup.Update(ref state);
        _cpEntityLookup.Update(ref state);
        _transformLookup.Update(ref state);
        _ltwLookup.Update(ref state);
        _damageOnContactLookup.Update(ref state);
        _destroyOnContactLookup.Update(ref state);
        _invincibleLookup.Update(ref state);
        _dashIFramesLookup.Update(ref state);
        _globalIFramesLookup.Update(ref state);
        _hitMemoryLookup.Update(ref state);
        _ricochetLookup.Update(ref state);
        _pierceLookup.Update(ref state);
        _coreStatsLookup.Update(ref state);
        _linearMovementLookup.Update(ref state);
        _followMovementLookup.Update(ref state);
        _subSpellRootLookup.Update(ref state);
        _activeSpellBufferLookup.Update(ref state);
        _summonLookup.Update(ref state);
        _colliderLookup.Update(ref state);
        _bossLookup.Update(ref state);
        _lifetimeLookup.Update(ref state);
        _statusEffectRequestLookup.Update(ref state);
        _spellLineageLookup.Update(ref state);
        _emittedHitRulesLookup.Update(ref state);
        _reHitDelayLookup.Update(ref state);
        _liveSpellStatsLookup.Update(ref state);

        var playerEntity = SystemAPI.GetSingletonEntity<Player>();

        // Second command buffer, dedicated to ResolveHit (a parallel writer): keeps its records off the
        // plain `ecb` used for bounce/pierce/destroy/explosion so the two writer flavors don't mix on one
        // buffer. Both play back at EndSimulation.
        var ecbResolve = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
        var resolveContext = new ResolveHitContext
        {
            EffectConfig = SystemAPI.GetSingleton<EffectTypeConfig>().Blob,
            LifeStealConversion = lifeStealConversion,
            PlayerEntity = playerEntity,
            LtwLookup = _ltwLookup,
            CoreStatsLookup = _coreStatsLookup,
            ActiveSpellLookup = _activeSpellBufferLookup,
            SummonLookup = _summonLookup,
            DamageEventsWriter = _damageEventsQueue.AsParallelWriter(),
            RequestLookup = _statusEffectRequestLookup,
            Emitter = SystemAPI.TryGetSingleton<EmitterConfig>(out var emitterCfg) ? emitterCfg : EmitterConfig.Default,
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
            TeamLookup = _teamLookup,
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
            GlobalIFramesLookup = _globalIFramesLookup,

            SpellSourceLookup = _subSpellRootLookup,
            SpellLineageLookup = _spellLineageLookup,
            EmittedHitRulesLookup = _emittedHitRulesLookup,
            ReHitDelayLookup = _reHitDelayLookup,
            LiveSpellStatsLookup = _liveSpellStatsLookup,

            QueueEntity = SystemAPI.TryGetSingletonEntity<EmitRequestQueue>(out var queue) ? queue : Entity.Null,
            SpellDb = SystemAPI.TryGetSingleton<SpellsDatabase>(out var spellDb) ? spellDb.Blobs : default,

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
            SummonLookup = _summonLookup,
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
        [ReadOnly] public ComponentLookup<Team> TeamLookup;
        [ReadOnly] public ComponentLookup<Destructible> DestructibleLookup;

        [ReadOnly] public ComponentLookup<DamageOnContact> DamageOnContactLookup;
        [ReadOnly] public ComponentLookup<DestroyOnContact> DestroyOnContactLookup;
        [ReadOnly] public ComponentLookup<Invincible> InvincibleLookup;
        [ReadOnly] public ComponentLookup<DashIFrames> DashIFramesLookup;
        // Write-enabled (I1 fix) — see OnCreate's comment on _globalIFramesLookup.
        public ComponentLookup<GlobalIFrames> GlobalIFramesLookup;

        public BufferLookup<HitEntityMemory> HitMemoryLookup;

        [ReadOnly] public ComponentLookup<LocalTransform> LocalTransformLookup;
        public ComponentLookup<LinearMovement> LinearMovementLookup;
        public ComponentLookup<FollowTargetMovement> FollowMovementLookup;
        public ComponentLookup<Bounce> BounceLookup;
        public ComponentLookup<Pierce> PierceLookup;

        [ReadOnly] public ComponentLookup<SpellSource> SpellSourceLookup;
        [ReadOnly] public ComponentLookup<SpellLineage> SpellLineageLookup;
        [ReadOnly] public ComponentLookup<EmittedHitRules> EmittedHitRulesLookup;
        [ReadOnly] public ComponentLookup<PhysicsCollider> ColliderLookup;
        [ReadOnly] public ComponentLookup<Boss> BossLookup;
        public ComponentLookup<Lifetime> LifetimeLookup;

        [ReadOnly] public ComponentLookup<ReHitDelay> ReHitDelayLookup;
        [ReadOnly] public ComponentLookup<LiveSpellStats> LiveSpellStatsLookup;

        // Emitters (OnImpact): EmitRequest queue singleton (Entity.Null = no emission system) and the spell DB.
        public Entity QueueEntity;
        [ReadOnly] public BlobAssetReference<SpellBlobs> SpellDb;

        private const int MaxHitMemoryEntries = 16; // bounded window — piercing/bouncing rarely exceeds this per projectile lifetime.

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
                    double reHitDelay = ReHitDelayLookup.TryGetComponent(damagerEntity, out var rhd)
                        ? rhd.Value
                        : Resolve.Emitter.DefaultReHitDelay;
                    bool asAlreadyHit = false;

                    for (var i = 0; i < history.Length; i++)
                    {
                        if (history[i].HitEntity == target)
                        {
                            asAlreadyHit = true;
                            // Pinned = the target that triggered this child's emission: never re-hit.
                            if (history[i].Pinned || CurrentTime - history[i].LastHitTime < reHitDelay)
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
                    {
                        if (history.Length >= MaxHitMemoryEntries)
                        {
                            // Evict the oldest NON-pinned entry; pinned entries are never evicted.
                            for (int k = 0; k < history.Length; k++)
                            {
                                if (!history[k].Pinned)
                                {
                                    history.RemoveAt(k);
                                    break;
                                }
                            }
                        }
                        if (history.Length < MaxHitMemoryEntries)
                            history.Add(new HitEntityMemory { HitEntity = target, LastHitTime = CurrentTime });
                    }
                }

                if (canDealDamage)
                {
                    // Dash i-frames: a target mid-dash with invincibility enabled ignores this hit.
                    bool dashInvincible = DashIFramesLookup.HasComponent(target)
                                          && DashIFramesLookup.IsComponentEnabled(target);

                    // Dash i-frames: the damager passes through untouched (dash reflect) — nothing is applied,
                    // and (below) no destroy/explode either, so the reflect can catch enemy projectiles.
                    // Invincible / GlobalIFrames (enabled by this same job after a damaging hit on the player,
                    // below; ticked down and disabled at 0 by GlobalIFramesSystem): the target still RECEIVES
                    // the hit (effects, emitters) with 0 damage (heuristics.md "Invincible = immune to damage,
                    // not to the hit").
                    bool damageImmune = ResolveHit.IsDamageImmune(target, in InvincibleLookup, in GlobalIFramesLookup);

                    // Crit result of this hit's Damage action (ResolveHit), carried by the OnImpact poster
                    // (TriggerCrit) so an emitted explosion inherits it (no second roll).
                    bool hitCrit = false;

                    if (!dashInvincible)
                    {
                        var random = HitRandom.CreateForHit(Seed, entityA, entityB);

                        int dbIndex = -1;
                        Entity caster = Entity.Null;
                        if (SpellSourceLookup.TryGetComponent(damagerEntity, out var spellSource))
                        {
                            dbIndex = spellSource.DatabaseIndex;
                            caster = spellSource.CasterEntity;
                        }

                        // The whole crit → damage buffer → tag effects → life steal → tracking bundle now
                        // lives in ResolveHit, shared with the area paths (kills the "crit gruyère").
                        // EffectsToApply is the list composed once at cast time — never
                        // re-derived from damageData.Tags (Tags is a read-only
                        // derived OUTPUT of that list, never an input to hit-time dispatch).
                        float hitDamage = damageData.Damage;
                        float hitCritChance = damageData.TotalCritChance;
                        float hitCritMult = damageData.TotalCritMultiplier;
                        ESpellTag hitTags = damageData.Tags;
                        var hitEffects = damageData.EffectsToApply;
                        // Attached children (FireOrbs): live stats of the ROOT ActiveSpell, read at hit time.
                        if (LiveSpellStatsLookup.HasComponent(damagerEntity)
                            && EmitterQueries.TryGetActiveSpell(Resolve.ActiveSpellLookup, caster, dbIndex, out var live))
                        {
                            hitDamage = live.FinalDamage;
                            hitCritChance = live.FinalCritChance;
                            hitCritMult = live.FinalCritDamageMultiplier;
                            hitTags = live.FinalTags;
                            hitEffects = live.FinalEffects;
                        }

                        var actions = new FixedList512Bytes<HitAction>();
                        actions.Add(HitAction.MakeDamage(hitDamage, hitCritChance, hitCritMult, hitTags));
                        for (int e = 0; e < hitEffects.Length; e++)
                            actions.Add(HitAction.MakeApplyEffect(hitEffects[e].Type));

                        byte generation = SpellLineageLookup.TryGetComponent(damagerEntity, out var lineage) ? lineage.Generation : (byte)0;
                        EmittedHitRulesLookup.TryGetComponent(damagerEntity, out var hitRules);

                        // Knockback pushes from the CASTER (a Summon pushes from itself); player position when the
                        // caster is gone or unknown — identical to eee06e4e for the player.
                        float3 pushOrigin = caster != Entity.Null && LocalTransformLookup.TryGetComponent(caster, out var casterTransform)
                            ? casterTransform.Position
                            : LocalTransformLookup[PlayerEntity].Position;
                        var hitSource = new HitSource
                        {
                            Caster = caster,
                            DatabaseIndex = dbIndex,
                            PushOrigin = pushOrigin,
                            Shake = ResolveShakeSource(damagerEntity, hitTags),
                            Emitter = damagerEntity,
                            DamageImmune = damageImmune,
                            Generation = generation,
                            CritMode = hitRules.CritMode,
                            LifeStealBlocked = hitRules.LifeStealBlocked,
                        };
                        hitCrit = ResolveHit.ApplyMany(in Resolve, ResolveECB, target.Index, target, in actions, in hitSource, ref random);

                        // Emitters (OnImpact): one request per resolved hit — pierce / bounce included, damage-immune
                        // target included. Player-side casts only. Position read now, before the ECB.
                        if (QueueEntity != Entity.Null && SpellDb.IsCreated && dbIndex >= 0 && TeamUtils.IsPlayerSide(caster, in TeamLookup, in PlayerLookup))
                        {
                            int shapeIndex = dbIndex;
                            Entity lineageId = damagerEntity;
                            byte emitGeneration = 0, depthLeft = 0;
                            if (SpellLineageLookup.TryGetComponent(damagerEntity, out var lin))
                            {
                                shapeIndex = lin.ShapeDatabaseIndex;
                                lineageId = lin.LineageId;
                                emitGeneration = lin.Generation;
                                depthLeft = lin.DepthLeft;
                            }

                            if (shapeIndex >= 0 && shapeIndex < SpellDb.Value.Spells.Length
                                && EmitterQueries.HasTrigger(ref SpellDb.Value.Spells[shapeIndex], EEmitterTrigger.OnImpact))
                            {
                                var damagerTransform = LocalTransformLookup[damagerEntity];
                                float3 direction = LinearMovementLookup.HasComponent(damagerEntity)
                                                   && LinearMovementLookup.IsComponentEnabled(damagerEntity)
                                    ? LinearMovementLookup[damagerEntity].Direction
                                    : damagerTransform.Forward();
                                var emitRng = HitRandom.CreateForHit(Seed ^ 0xA5A5A5A5u, entityA, entityB);

                                ECB.AppendToBuffer(QueueEntity, new EmitRequest
                                {
                                    RootDatabaseIndex = dbIndex,
                                    ShapeDatabaseIndex = shapeIndex,
                                    Caster = caster,
                                    Trigger = EEmitterTrigger.OnImpact,
                                    Position = damagerTransform.Position,
                                    Direction = direction,
                                    Rotation = damagerTransform.Rotation,
                                    Owner = Entity.Null,
                                    LineageId = lineageId,
                                    Generation = emitGeneration,
                                    DepthLeft = depthLeft,
                                    TriggerCrit = hitCrit,
                                    IgnoreTarget = target,
                                    RollSeed = emitRng.NextUInt(),
                                });
                            }
                        }

                        // GlobalIFrames + flash only for a hit that actually damaged (an immune hit must not
                        // refresh the i-frames forever).
                        if (!damageImmune)
                        {
                            // Global player i-frames: enabled after any damaging hit on the player (not enemies).
                            if (target == PlayerEntity)
                            {
                                // Live write (I1 fix): closes the same-frame gap where N simultaneous contacts
                                // in this one sequential job pass would otherwise all read "not immune yet" —
                                // the ECB-only enable below plays back at EndSimulation, one frame too late for
                                // this. Kept alongside the ECB write for consistency (and because GlobalIFrames
                                // isn't guaranteed present pre-bake on every target — HasComponent guards it).
                                if (GlobalIFramesLookup.HasComponent(target))
                                {
                                    GlobalIFramesLookup[target] = new GlobalIFrames { RemainingTime = GlobalIFrames.OnHitDuration };
                                    GlobalIFramesLookup.SetComponentEnabled(target, true);
                                }

                                ResolveECB.SetComponent(target.Index, target, new GlobalIFrames { RemainingTime = GlobalIFrames.OnHitDuration });
                                ResolveECB.SetComponentEnabled<GlobalIFrames>(target.Index, target, true);
                            }

                            // Feedbacks
                            ApplyFeedbacks(target);
                        }
                    }

                    // Only a dash's i-frames let the projectile pass through unharmed (so the dash's
                    // reflect can catch it). The debug Invincible tag still destroys projectiles on
                    // contact as before — it only cancels damage, not the impact.
                    bool shouldDestroy = !dashInvincible && DestroyOnContactLookup.HasComponent(damagerEntity);

                    // Cascade by priority: Bounce first (unchanged — matches Brotato/PoE, intentional, do not
                    // "clean up" this order in a future refactor), then Pierce as the fallback when Bounce is
                    // exhausted or has no target. Explode is an emitter now, posted after ApplyMany (OnImpact
                    // request above): independent of Bounce/Pierce, one request per resolved hit.
                    bool bounceConsumedHit = false;

                    if (!dashInvincible && CapabilityUtils.IsCapabilityActive(BounceLookup, damagerEntity))
                    {
                        var bounce = BounceLookup[damagerEntity];
                        if (bounce.RemainingBounces > 0
                            && TryFindNextUnvisitedTarget(damagerEntity, target, bounce.BounceRange, out Entity newTarget,
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
                            ECB.SetComponent(damagerEntity, bounce); // uniform ECB write — was a direct BounceLookup[damagerEntity] = bounce write.
                            shouldDestroy = false;
                            bounceConsumedHit = true;
                        }
                    }

                    if (!bounceConsumedHit && !dashInvincible && CapabilityUtils.IsCapabilityActive(PierceLookup, damagerEntity))
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
                        && (CapabilityUtils.IsCapabilityActive(BounceLookup, damagerEntity) || CapabilityUtils.IsCapabilityActive(PierceLookup, damagerEntity))
                        && LifetimeLookup.HasComponent(damagerEntity))
                    {
                        var lifetime = LifetimeLookup[damagerEntity];
                        lifetime.TimeLeft = lifetime.Duration;
                        ECB.SetComponent(damagerEntity, lifetime);
                    }

                    if (shouldDestroy && DestructibleLookup.HasComponent(damagerEntity))
                    {
                        ECB.SetComponentEnabled<DestroyEntityFlag>(damagerEntity, true);
                    }
                }
            }
        }

        private void ApplyFeedbacks(Entity hitEntity)
        {
            if (!DestructibleLookup.HasComponent(hitEntity))
                return;

            // var shakeReq = ECB.CreateEntity(0);
            // ECB.AddComponent<ShakeFeedbackRequest>(0, shakeReq);

            var flashReq = ECB.CreateEntity();
            ECB.AddComponent(flashReq, new HitFrameFeedbackSystem.HitFrameColorRequest { TargetEntity = hitEntity });
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
            // Hostiles of the instance's own team: its TargetLayers were TargetLayers(caster team) at cast, and survive the caster.
            uint hostileLayers = DamageOnContactLookup.TryGetComponent(projectile, out var damager)
                ? TeamUtils.HostileFromTargetLayers(damager.TargetLayers)
                : CollisionLayers.Enemy;
            var filter = new CollisionFilter
            {
                BelongsTo = CollisionLayers.Raycast,
                CollidesWith = hostileLayers
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

            // Hostiles of the instance's own team: its TargetLayers were TargetLayers(caster team) at cast, and survive the caster.
            uint hostileLayers = DamageOnContactLookup.TryGetComponent(projectile, out var damager)
                ? TeamUtils.HostileFromTargetLayers(damager.TargetLayers)
                : CollisionLayers.Enemy;
            var filter = new CollisionFilter
            {
                BelongsTo = CollisionLayers.Raycast,
                CollidesWith = hostileLayers
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
}

public struct SpellDamageEvent
{
    public int DatabaseIndex;
    public int DamageAmount;
    /// <summary>Who dealt it: the player (credited as before), a Summon (credited to its owner's Summon spell), anything else (ignored).</summary>
    public Entity Caster;
}