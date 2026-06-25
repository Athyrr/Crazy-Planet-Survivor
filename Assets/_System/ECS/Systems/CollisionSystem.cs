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

    private ComponentLookup<DamageOnContact> _damageOnContactLookup;
    private ComponentLookup<DestroyOnContact> _destroyOnContactLookup;
    private ComponentLookup<Invincible> _invincibleLookup;
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

        _damageOnContactLookup = state.GetComponentLookup<DamageOnContact>(true);
        _destroyOnContactLookup = state.GetComponentLookup<DestroyOnContact>(true);
        _invincibleLookup = state.GetComponentLookup<Invincible>(true);
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

        _slowLookup.Update(ref state);
        _stunLookup.Update(ref state);
        _burnLookup.Update(ref state);
        _playerLookup.Update(ref state);
        _cpEntityLookup.Update(ref state);
        _transformLookup.Update(ref state);
        _damageOnContactLookup.Update(ref state);
        _destroyOnContactLookup.Update(ref state);
        _invincibleLookup.Update(ref state);
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

        var playerEntity = SystemAPI.GetSingletonEntity<Player>();

        var triggerCollisionJob = new TriggerCollisionJob
        {
            Seed = (uint)(SystemAPI.Time.ElapsedTime * 1000) + 1,

            ECB = ecb,
            CurrentTime = SystemAPI.Time.ElapsedTime,
            CollisionWorld = physicsWorld.CollisionWorld,

           PlayerEntity = playerEntity,

            // EffectsConfig = _effectsConfig,
            EffectsConfig = effectsConfig,

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
            ExplodeOnContactLookup = _explodeLookup,

            SlowLookup = _slowLookup,
            StunLookup = _stunLookup,
            BurnLookup = _burnLookup,
            KnockbackLookup = _knockbackLookup,

            SpellSourceLookup = _subSpellRootLookup,
            DamageEventsWriter = _damageEventsQueue,

            ColliderLookup = _colliderLookup,
            BossLookup = _bossLookup
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
        public double CurrentTime;
        [ReadOnly] public CollisionWorld CollisionWorld;

        public Entity PlayerEntity;


        [ReadOnly] public ActiveEffectsConfig EffectsConfig;

        [ReadOnly] public ComponentLookup<Player> PlayerLookup;
        [ReadOnly] public ComponentLookup<Destructible> DestructibleLookup;

        [ReadOnly] public ComponentLookup<DamageOnContact> DamageOnContactLookup;
        [ReadOnly] public ComponentLookup<DestroyOnContact> DestroyOnContactLookup;
        [ReadOnly] public ComponentLookup<Invincible> InvincibleLookup;

        [ReadOnly] public ComponentLookup<SlowEffect> SlowLookup;
        [ReadOnly] public ComponentLookup<StunEffect> StunLookup;
        [ReadOnly] public ComponentLookup<BurnEffect> BurnLookup;

        public BufferLookup<HitEntityMemory> HitMemoryLookup;

        [ReadOnly] public ComponentLookup<LocalTransform> LocalTransformLookup;
        public ComponentLookup<LinearMovement> LinearMovementLookup;
        public ComponentLookup<FollowTargetMovement> FollowMovementLookup;
        public ComponentLookup<Bounce> BounceLookup;
        public ComponentLookup<Pierce> PierceLookup;
        [ReadOnly] public ComponentLookup<ActiveKnockback> KnockbackLookup;
        [ReadOnly] public ComponentLookup<ExplodeOnContact> ExplodeOnContactLookup;

        public NativeQueue<SpellDamageEvent> DamageEventsWriter;
        [ReadOnly] public ComponentLookup<SpellSource> SpellSourceLookup;
        [ReadOnly] public ComponentLookup<PhysicsCollider> ColliderLookup;
        [ReadOnly] public ComponentLookup<Boss> BossLookup;

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
                    // todo let target receive damge even if invincible. Consume damage on Health system and avoid health loss instead
                    if (!InvincibleLookup.HasComponent(target))
                    {
                        var random = Random.CreateFromIndex((Seed ^ ((uint)entityA.Index * 0x9E3779B1u) ^ ((uint)entityB.Index * 0x85EBCA77u)) | 1u);

                        bool isCrit = random.NextFloat(0f, 1f) <= damageData.TotalCritChance;
                        float criticalDamagesMultiplier = 1f;
                        if (isCrit)
                            criticalDamagesMultiplier = math.max(1.0f, damageData.TotalCritMultiplier);

                        int damageDealt = (int)(damageData.Damage * criticalDamagesMultiplier);

                        ECB.AppendToBuffer(
                            target,
                            new DamageBufferElement
                            {
                                Damage = damageDealt,
                                Tag = damageData.Tags,
                                IsCritical = isCrit,
                                ShakeSource = ResolveShakeSource(damagerEntity, damageData.Tags),
                            }
                        );

                        // Active effects using tags

                        if ((damageData.Tags & ESpellTag.Slow) != 0)
                        {
                            var slow = new SlowEffect
                            {
                                SpeedReductionMultiplier = EffectsConfig.BaseSlowMultiplier,
                                DurationLeft = EffectsConfig.SlowDuration
                            };

                            if (SlowLookup.HasComponent(target))
                            {
                                ECB.SetComponent(target, slow);
                                ECB.SetComponentEnabled<SlowEffect>(target, true);
                            }
                            else
                            {
                                ECB.AddComponent(target, slow);
                            }
                        }

                        if ((damageData.Tags & ESpellTag.Stun) != 0)
                        {
                            var stun = new StunEffect
                            {
                                DurationLeft = EffectsConfig.StunDuration
                            };

                            if (StunLookup.HasComponent(target))
                            {
                                ECB.SetComponent(target, stun);
                                ECB.SetComponentEnabled<StunEffect>(target, true);
                            }
                            else
                            {
                                ECB.AddComponent(target, stun);
                            }
                        }

                        if ((damageData.Tags & ESpellTag.Knockback) != 0)
                        {
                            // float3 damagerPos = LocalTransformLookup[damagerEntity].Position;
                            float3 targetPos = LocalTransformLookup[target].Position;

                            // Normalize direction (push the target away from the player)
                            float3 pushDir = targetPos - LocalTransformLookup[PlayerEntity].Position;
                            float distSq = math.lengthsq(pushDir);

                            if (distSq > 0.001f)
                                pushDir = math.normalize(pushDir);
                            else
                                pushDir = LocalTransformLookup[damagerEntity].Forward();


                            var kbData = new ActiveKnockback
                            {
                                Direction = pushDir,
                                InitialForce = EffectsConfig.KnockbackForce, // ex: 15f
                                DurationLeft = EffectsConfig.KnockbackDuration, // ex: 0.3f
                                MaxDuration = EffectsConfig.KnockbackDuration
                            };

                           if (KnockbackLookup.HasComponent(target))
                            {
                                ECB.SetComponent(target, kbData);
                                ECB.SetComponentEnabled<ActiveKnockback>(target, true);
                            }
                            else
                            {
                                ECB.AddComponent(target, kbData);
                            }
                        }

                        if ((damageData.Tags & ESpellTag.Burn) != 0)
                        {
                            var burn = new BurnEffect
                            {
                                DamageOnTick = EffectsConfig.BurnDamageRatio * damageData.Damage,
                                TickRate = EffectsConfig.BurnTickRate,
                                TickTimer = 0f,
                                RemainingTime = EffectsConfig.BurnDuration
                            };

                            if (BurnLookup.HasComponent(target))
                            {
                                ECB.SetComponent(target, burn);
                                ECB.SetComponentEnabled<BurnEffect>(target, true);
                            }
                            else
                            {
                                ECB.AddComponent(target, burn);
                            }
                        }

                        if (SpellSourceLookup.TryGetComponent(damagerEntity, out var spellSource))
                        {
                            DamageEventsWriter.Enqueue(new SpellDamageEvent
                            {
                                DatabaseIndex = spellSource.DatabaseIndex,
                                DamageAmount = (int)damageDealt
                            });
                        }

                        // Feedbacks
                        ApplyFeedbacks(target);
                    }

                    if (ExplodeOnContactLookup.TryGetComponent(damagerEntity, out var explosion) &&
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

                    bool shouldDestroy = DestroyOnContactLookup.HasComponent(damagerEntity);

                    if (BounceLookup.HasComponent(damagerEntity))
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

                    else if (PierceLookup.HasComponent(damagerEntity))
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