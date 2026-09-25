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

            TickBurnDamage(chunkIndex, entity, anyBurn, strongestBurnMag, strongestBurnSource);
        }

        /// <summary>Routes Burn tick damage through the same core as direct hits: tracking always,
        /// crit/life-steal gated by EffectTypeConfig + the matching CoreStats upgrade toggle. Ticks are not
        /// paced independently here — TickRate gating is intentionally left to a per-instance timer living
        /// alongside RemainingTime would double the buffer's footprint for a fixed cadence; instead this
        /// job ticks every frame it finds an active Burn and applies EffectConfig.TickRate-scaled damage
        /// (DamageOnTick × DeltaTime / TickRate), matching a rate-equivalent continuous tick — avoids a
        /// second per-instance timer field while preserving the same damage-per-second.</summary>
        private void TickBurnDamage(int chunkIndex, Entity entity, bool anyBurn, float damagePerTick, Entity burnSource)
        {
            if (!anyBurn || damagePerTick <= 0f)
                return;

            ref var entries = ref EffectConfig.Value.Entries;
            ref readonly var cfg = ref EffectTypeConfigLookup.Get(ref entries, EffectType.Burn);
            if (cfg.TickRate <= 0f)
                return;

            float damageThisFrame = damagePerTick * (DeltaTime / cfg.TickRate);
            if (damageThisFrame < 1f)
                return; // sub-1 damage this frame — DamageBufferElement truncates to int; skip rather than deal 0.

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
            var action = HitAction.MakeDamage(damageThisFrame, critChance, critMult, ESpellTag.Burn);
            var source = new HitSource { Caster = caster, DatabaseIndex = dbIndex, PushOrigin = default, Shake = EDamageShakeSource.DoT, Emitter = burnSource };
            ResolveHit.ApplyDamage(in Resolve, ECB, chunkIndex, entity, in action, in source, ref rng, allowCrit, allowLifeSteal);
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
            var sums = new NativeHashMap<int, int>(16, Allocator.Temp);
            while (DamageEventsQueue.TryDequeue(out var evt))
            {
                if (sums.ContainsKey(evt.DatabaseIndex))
                    sums[evt.DatabaseIndex] += evt.DamageAmount;
                else
                    sums.Add(evt.DatabaseIndex, evt.DamageAmount);
            }

            if (PlayerEntity != Entity.Null && ActiveSpellLookup.TryGetBuffer(PlayerEntity, out var buffer))
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
