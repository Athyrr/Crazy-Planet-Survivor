using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(EntitiesMovementSystem))]
[BurstCompile]
public partial struct ActiveEffectsSystem : ISystem
{
    private ComponentLookup<SlowEffect> _slowEffectLookup;
    private ComponentLookup<StunEffect> _stunEffectLookup;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();

        // todo job for other effects
        _slowEffectLookup = state.GetComponentLookup<SlowEffect>();
        _stunEffectLookup = state.GetComponentLookup<StunEffect>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecbBurn = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter();
        var ecbCalculate = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter();

        var dt = SystemAPI.Time.DeltaTime;

        // Update lookups
        _slowEffectLookup.Update(ref state);
        _stunEffectLookup.Update(ref state);

        // todo job for other effects

        var processBurnJob = new BurnJob
        {
            DeltaTime = dt,
        };
        var burnHandle = processBurnJob.ScheduleParallel(state.Dependency);

        var updateSlowJob = new UpdateSlowJob()
        {
            DeltaTime = dt,
        };
        var slowHandle = updateSlowJob.ScheduleParallel(state.Dependency);

        var updateStunJob = new UpdateStunJob()
        {
            DeltaTime = dt
        };
        var stunHandle = updateStunJob.ScheduleParallel(state.Dependency);

        // Combine effects handle
        var combinedEffectsHandle = JobHandle.CombineDependencies(burnHandle, slowHandle, stunHandle);

        // Compose LiveStats from CoreStats + CharacterStatBuff sum + dedicated effects (Slow).
        // This is the tick-per-frame composition; the 12 spell stats are recomposed on-demand by
        // SpellStatsCalculationSystem (see §9.2 of SPELL_TAXONOMY).
        var statsHandle = new ComposeLiveStatsJob
        {
            SlowEffectLookup = _slowEffectLookup,
        }.ScheduleParallel(combinedEffectsHandle);

        state.Dependency = statsHandle;
    }

    [BurstCompile]
    [WithAll(typeof(BurnEffect))]
    private partial struct BurnJob : IJobEntity
    {
        [ReadOnly] public float DeltaTime;

        private void Execute([ChunkIndexInQuery] int chunkIndex, Entity entity,
            ref BurnEffect burn, EnabledRefRW<BurnEffect> burnEnabled,
            ref DynamicBuffer<DamageBufferElement> damageBuffer)
        {
            burn.RemainingTime -= DeltaTime;

            if (burn.RemainingTime <= 0)
            {
                burnEnabled.ValueRW = false;
                return;
            }

            burn.TickTimer += DeltaTime;
            if (burn.TickTimer >= burn.TickRate)
            {
                burn.TickTimer -= burn.TickRate;

                damageBuffer.Add(new DamageBufferElement()
                {
                    Damage = (int)burn.DamageOnTick,
                    Tag = ESpellTag.Burn,
                    IsCritical = false, // todo crit burn
                    ShakeSource = EDamageShakeSource.DoT,
                });
            }
        }
    }

    [BurstCompile]
    private partial struct UpdateSlowJob : IJobEntity
    {
        [ReadOnly] public float DeltaTime;

        private void Execute(ref SlowEffect slow, EnabledRefRW<SlowEffect> slowEnabled)
        {
            slow.DurationLeft -= DeltaTime;
            if (slow.DurationLeft <= 0)
            {
                slowEnabled.ValueRW = false;
            }
        }
    }

    [BurstCompile]
    private partial struct UpdateStunJob : IJobEntity
    {
        [ReadOnly] public float DeltaTime;

        private void Execute(ref StunEffect stun, EnabledRefRW<StunEffect> stunEnabled)
        {
            stun.DurationLeft -= DeltaTime;
            if (stun.DurationLeft <= 0)
            {
                stunEnabled.ValueRW = false;
            }
        }
    }

    /// <summary>
    /// Composes <see cref="LiveStats"/> from <c>CoreStats</c> + sum of <see cref="CharacterStatBuff"/>
    /// entries + dedicated effects (currently Slow). Additive composition (anti-exploit): buff+debuff
    /// deltas add up, never multiply. Runs each frame; consumers read LiveStats, never CoreStats direct.
    /// </summary>
    [BurstCompile]
    private partial struct ComposeLiveStatsJob : IJobEntity
    {
        [ReadOnly] public ComponentLookup<SlowEffect> SlowEffectLookup;

        public void Execute([ChunkIndexInQuery] int chunkIndex, Entity entity, in CoreStats coreStats,
            in DynamicBuffer<CharacterStatBuff> buffs, ref LiveStats liveStats)
        {
            // Sum active buff/debuff deltas per stat (single pass over the buffer).
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

            // Dedicated effect: Slow contribution to move speed (kept out of the buff buffer because
            // it stacks "strongest wins" from many sources — additive would explode in a mob crowd).
            float slowReduction = 0f;
            if (SlowEffectLookup.TryGetComponent(entity, out var slow) &&
                SlowEffectLookup.IsComponentEnabled(entity))
            {
                slowReduction = slow.SpeedReductionMultiplier;
            }

            // Final composition — additive, never multiplicative (anti-stacking exploit).
            liveStats.MoveSpeed   = coreStats.BaseMoveSpeed   * (1f + coreStats.MoveSpeed   + moveSpeedBuff   - slowReduction);
            liveStats.PickupRange = coreStats.BasePickupRange * (1f + coreStats.PickupRange + pickupRangeBuff);
            liveStats.Armor       = coreStats.BaseArmor + coreStats.Armor + armorBuff;
            liveStats.HealthRegen = coreStats.HealthRegen + regenBuff;
            liveStats.KBResist    = coreStats.KnockbackResistance + kbResistBuff;
        }
    }
}