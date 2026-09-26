using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(EntitiesMovementSystem))]
[BurstCompile]
public partial struct ActiveEffectsSystem : ISystem
{
    private ComponentLookup<SlowState> _slowStateLookup;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        state.RequireForUpdate<EffectTypeConfig>();

        _slowStateLookup = state.GetComponentLookup<SlowState>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecbTick = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
        var ecbCalculate = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter();

        var dt = SystemAPI.Time.DeltaTime;
        var effectConfig = SystemAPI.GetSingleton<EffectTypeConfig>().Blob;
        var playerEntity = SystemAPI.HasSingleton<Player>() ? SystemAPI.GetSingletonEntity<Player>() : Entity.Null;
        float lifeStealConversion = SystemAPI.TryGetSingleton<LifeStealConfig>(out var lsCfg) ? lsCfg.Conversion : 0.075f;
        uint seed = (uint)(SystemAPI.Time.ElapsedTime * 1000) + 1;

        _slowStateLookup.Update(ref state);

        // Damage tracking queue for Burn ticks routed through ResolveHit.ApplyDamage.
        var damageEvents = new NativeQueue<SpellDamageEvent>(Allocator.TempJob);

        var resolveCtx = new ResolveHitContext
        {
            EffectConfig = effectConfig,
            LifeStealConversion = lifeStealConversion,
            PlayerEntity = playerEntity,
            LtwLookup = SystemAPI.GetComponentLookup<Unity.Transforms.LocalToWorld>(true),
            CoreStatsLookup = SystemAPI.GetComponentLookup<CoreStats>(true),
            ActiveSpellLookup = SystemAPI.GetBufferLookup<ActiveSpell>(true),
            DamageEventsWriter = damageEvents.AsParallelWriter(),
            // Not exercised today (TickBurnDamage only ever calls ResolveHit.ApplyDamage directly, never
            // ApplyEffect) — populated anyway since this is the same shared ResolveHitContext struct
            // CollisionSystem/AreaAttackSystem populate it in; leaving it default would be a latent trap
            // for whatever calls ApplyEffect through this context next.
            RequestLookup = SystemAPI.GetBufferLookup<StatusEffectApplyRequest>(true),
        };

        // Phase 1: drain this frame's StatusEffectApplyRequest into the real StatusEffectInstance buffer.
        // Parallel-safe: one entity's own two buffers per job iteration, never shared across entities.
        var drainHandle = new DrainApplyRequestsJob().ScheduleParallel(state.Dependency);

        // Phase 2: tick every active instance, expire, and derive the 4 mirror components from the
        // strongest instance of each type. [WithPresent] on all 4 mirrors so the job can re-enable one
        // that's currently disabled (a fresh effect landing on a target with no active effect this frame) —
        // same pattern as this project's own DashSystem.DashJob for DashIFrames.
        var tickHandle = new TickStatusEffectsJob
        {
            DeltaTime = dt,
            Seed = seed,
            EffectConfig = effectConfig,
            Resolve = resolveCtx,
            ECB = ecbTick.AsParallelWriter(),
            SpellSourceLookup = SystemAPI.GetComponentLookup<SpellSource>(true),
        }.ScheduleParallel(drainHandle);

        var trackHandle = new TrackDamageJob
        {
            DamageEventsQueue = damageEvents,
            ActiveSpellLookup = SystemAPI.GetBufferLookup<ActiveSpell>(false),
            PlayerEntity = playerEntity,
        }.Schedule(tickHandle);

        var statsHandle = new ComposeLiveStatsJob
        {
            SlowStateLookup = _slowStateLookup,
        }.ScheduleParallel(trackHandle);

        state.Dependency = damageEvents.Dispose(statsHandle);
    }

    [BurstCompile]
    private partial struct DrainApplyRequestsJob : IJobEntity
    {
        private void Execute(ref DynamicBuffer<StatusEffectInstance> instances, ref DynamicBuffer<StatusEffectApplyRequest> requests)
        {
            if (requests.Length == 0)
                return;

            for (int i = 0; i < requests.Length; i++)
            {
                var r = requests[i];
                StatusEffectUtility.ApplyOrRefresh(ref instances, r.Type, r.Source, r.Magnitude, r.Duration,
                    r.Direction, r.StackMode, r.MaxStacks);
            }

            requests.Clear();
        }
    }

    [BurstCompile]
    [WithPresent(typeof(SlowState), typeof(StunState), typeof(BurnState), typeof(KnockbackState))]
    private partial struct TickStatusEffectsJob : IJobEntity
    {
        [ReadOnly] public float DeltaTime;
        [ReadOnly] public uint Seed;
        [ReadOnly] public BlobAssetReference<EffectTypeConfigBlob> EffectConfig;
        [ReadOnly] public ComponentLookup<SpellSource> SpellSourceLookup;
        public ResolveHitContext Resolve;
        public EntityCommandBuffer.ParallelWriter ECB;

        private void Execute([ChunkIndexInQuery] int chunkIndex, Entity entity,
            ref DynamicBuffer<StatusEffectInstance> instances,
            ref SlowState slowState, EnabledRefRW<SlowState> slowEnabled,
            ref StunState stunState, EnabledRefRW<StunState> stunEnabled,
            ref BurnState burnState, EnabledRefRW<BurnState> burnEnabled,
            ref KnockbackState knockbackState, EnabledRefRW<KnockbackState> knockbackEnabled)
        {
            float strongestSlow = 0f, strongestStunRemaining = 0f, strongestBurnMag = 0f, strongestBurnRemaining = 0f;
            bool anySlow = false, anyStun = false, anyBurn = false, anyKnockback = false;
            Entity strongestBurnSource = Entity.Null;
            float3 kbDirection = float3.zero;
            float kbForce = 0f, kbRemaining = 0f;

            // C1 fix: fetched once so every Burn instance can accumulate its own fractional-damage carry
            // every frame (not just the strongest), regardless of which source ends up "strongest" below.
            ref var burnEntries = ref EffectConfig.Value.Entries;
            ref readonly var burnCfg = ref EffectTypeConfigLookup.Get(ref burnEntries, EffectType.Burn);

            for (int i = instances.Length - 1; i >= 0; i--)
            {
                var inst = instances[i];
                inst.RemainingTime -= DeltaTime;

                if (inst.RemainingTime <= 0f)
                {
                    instances.RemoveAt(i);
                    continue;
                }

                switch (inst.Type)
                {
                    case EffectType.Slow:
                        anySlow = true;
                        strongestSlow = math.max(strongestSlow, inst.Magnitude);
                        break;

                    case EffectType.Stun:
                        anyStun = true;
                        strongestStunRemaining = math.max(strongestStunRemaining, inst.RemainingTime);
                        break;

                    case EffectType.Knockback:
                        if (!anyKnockback || inst.RemainingTime > kbRemaining)
                        {
                            anyKnockback = true;
                            kbDirection = inst.Direction;
                            kbForce = inst.Magnitude;
                            kbRemaining = inst.RemainingTime;
                        }
                        break;

                    case EffectType.Burn:
                        anyBurn = true;
                        // C1 fix: carry this frame's fractional damage into the instance's own
                        // accumulator — every active Burn instance accumulates every frame (not only
                        // the one currently strongest), so a source's residual is already correct by the
                        // time it becomes strongest.
                        if (burnCfg.TickRate > 0f)
                            inst.DamageAccumulator += inst.Magnitude * (DeltaTime / burnCfg.TickRate);

                        if (inst.Magnitude > strongestBurnMag)
                        {
                            strongestBurnMag = inst.Magnitude;
                            strongestBurnSource = inst.Source;
                        }
                        strongestBurnRemaining = math.max(strongestBurnRemaining, inst.RemainingTime);
                        break;
                }

                instances[i] = inst;
            }

            slowState.CurrentMultiplier = strongestSlow;
            slowEnabled.ValueRW = anySlow;

            stunState.RemainingTime = strongestStunRemaining;
            stunEnabled.ValueRW = anyStun;

            burnState.CurrentDamagePerTick = strongestBurnMag;
            burnState.RemainingTime = strongestBurnRemaining;
            burnEnabled.ValueRW = anyBurn;

            if (anyKnockback)
            {
                if (!knockbackEnabled.ValueRO)
                {
                    knockbackState.Direction = kbDirection;
                    knockbackState.InitialForce = kbForce;
                    knockbackState.MaxDuration = kbRemaining;
                }
                knockbackState.RemainingTime = kbRemaining;
            }
            knockbackEnabled.ValueRW = anyKnockback;

            // C1 fix: flush the strongest Burn instance's accumulator — matched by Source (not index:
            // instances.RemoveAt above can shift indices, so re-scanning the post-removal buffer is the
            // only safe way to find it again). Only the strongest instance emits a hit this frame, same
            // "strongest wins" single-hit-per-frame cadence as before this fix — untouched by it.
            float damageThisTick = 0f;
            if (anyBurn)
            {
                for (int i = 0; i < instances.Length; i++)
                {
                    if (instances[i].Type != EffectType.Burn || instances[i].Source != strongestBurnSource)
                        continue;

                    var strongestInst = instances[i];
                    damageThisTick = math.floor(strongestInst.DamageAccumulator);
                    if (damageThisTick >= 1f)
                    {
                        strongestInst.DamageAccumulator -= damageThisTick;
                        instances[i] = strongestInst;
                    }
                    break;
                }
            }

            TickBurnDamage(chunkIndex, entity, anyBurn, damageThisTick, strongestBurnSource);
        }

        /// <summary>Routes Burn tick damage through the same core as direct hits: tracking always,
        /// crit/life-steal gated by EffectTypeConfig + the matching CoreStats upgrade toggle. Ticks are not
        /// paced independently here — TickRate gating is intentionally left to a per-instance timer living
        /// alongside RemainingTime would double the buffer's footprint for a fixed cadence; instead this
        /// job ticks every frame it finds an active Burn and applies EffectConfig.TickRate-scaled damage
        /// (DamageOnTick × DeltaTime / TickRate), matching a rate-equivalent continuous tick — avoids a
        /// second per-instance timer field while preserving the same damage-per-second. <paramref
        /// name="damage"/> arrives already flushed to a whole number by the caller's per-instance
        /// DamageAccumulator (C1 fix) — the fractional remainder that used to be discarded every frame now
        /// carries over until it crosses 1, so Burn still deals its configured damage-per-second instead of
        /// zero.</summary>
        private void TickBurnDamage(int chunkIndex, Entity entity, bool anyBurn, float damage, Entity burnSource)
        {
            if (!anyBurn || damage < 1f)
                return;

            ref var entries = ref EffectConfig.Value.Entries;
            ref readonly var cfg = ref EffectTypeConfigLookup.Get(ref entries, EffectType.Burn);

            // Resolve the emitting entity's SpellSource (caster + DB index) for tracking/crit. A hazard
            // zone's Burn has no SpellSource -- TryGetComponent returns false, caster/dbIndex stay at their
            // "no attribution" defaults, matching HitSource.DatabaseIndex's documented -1 convention.
            Entity caster = Entity.Null;
            int dbIndex = -1;
            if (burnSource != Entity.Null && SpellSourceLookup.TryGetComponent(burnSource, out var spellSource))
            {
                caster = spellSource.CasterEntity;
                dbIndex = spellSource.DatabaseIndex;
            }

            bool allowCrit = cfg.AllowCrit;
            bool allowLifeSteal = cfg.AllowLifeSteal;
            if (caster != Entity.Null && Resolve.CoreStatsLookup.HasComponent(caster))
            {
                var coreStats = Resolve.CoreStatsLookup[caster];
                allowCrit = allowCrit || coreStats.BurnCanCrit > 0f;
                allowLifeSteal = allowLifeSteal || coreStats.BurnCanLifeSteal > 0f;
            }

            float critChance = 0f, critMult = 1f;
            if (allowCrit && dbIndex >= 0 && Resolve.ActiveSpellLookup.TryGetBuffer(caster, out var casterSpells))
            {
                for (int i = 0; i < casterSpells.Length; i++)
                {
                    if (casterSpells[i].DatabaseIndex != dbIndex)
                        continue;
                    critChance = casterSpells[i].FinalCritChance;
                    critMult = casterSpells[i].FinalCritDamageMultiplier;
                    break;
                }
            }

            var rng = HitRandom.CreateForHit(Seed, entity, burnSource);
            var action = HitAction.MakeDamage(damage, critChance, critMult, ESpellTag.Burn);
            var source = new HitSource { Caster = caster, DatabaseIndex = dbIndex, PushOrigin = default, Shake = EDamageShakeSource.DoT, Emitter = burnSource };
            ResolveHit.ApplyDamage(in Resolve, ECB, chunkIndex, entity, in action, in source, ref rng, allowCrit, allowLifeSteal);
        }
    }

    // I4 fix: the nested TrackDamageJob that used to live here is gone — this system now uses the shared
    // top-level TrackDamageJob (Assets/_System/ECS/Systems/TrackDamageJob.cs, extracted by Task 21 from
    // CollisionSystem and already reused by AreaAttackSystem). This file predates that extraction (Task 9,
    // before Task 21 existed) and was never repointed at it. Same namespace, same shape — the construction
    // site in OnUpdate resolves to the shared type unchanged.

    [BurstCompile]
    private partial struct ComposeLiveStatsJob : IJobEntity
    {
        [ReadOnly] public ComponentLookup<SlowState> SlowStateLookup;

        public void Execute([ChunkIndexInQuery] int chunkIndex, Entity entity, in CoreStats coreStats,
            in DynamicBuffer<CharacterStatBuff> buffs, ref LiveStats liveStats)
        {
            float moveSpeedBuff = 0f, pickupRangeBuff = 0f, armorBuff = 0f, regenBuff = 0f, kbResistBuff = 0f;
            for (int i = 0; i < buffs.Length; i++)
            {
                var b = buffs[i];
                switch (b.Stat)
                {
                    case ECharacterStat.Speed:               moveSpeedBuff   += b.Value; break;
                    case ECharacterStat.CollectRange:        pickupRangeBuff += b.Value; break;
                    case ECharacterStat.Armor:               armorBuff       += b.Value; break;
                    case ECharacterStat.HealthRegen:         regenBuff       += b.Value; break;
                    case ECharacterStat.KnockbackResistance: kbResistBuff    += b.Value; break;
                }
            }

            float slowReduction = 0f;
            if (SlowStateLookup.TryGetComponent(entity, out var slow) && SlowStateLookup.IsComponentEnabled(entity))
                slowReduction = slow.CurrentMultiplier;

            liveStats.MoveSpeed   = coreStats.BaseMoveSpeed   * (1f + coreStats.MoveSpeed   + moveSpeedBuff   - slowReduction);
            liveStats.PickupRange = coreStats.BasePickupRange * (1f + coreStats.PickupRange + pickupRangeBuff);
            liveStats.Armor       = coreStats.BaseArmor + coreStats.Armor + armorBuff;
            liveStats.HealthRegen = coreStats.HealthRegen + regenBuff;
            liveStats.KBResist    = coreStats.KnockbackResistance + kbResistBuff;
        }
    }
}
