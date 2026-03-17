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

        // Calculate final stats after all
        var statsHandle = new CalculateFinalStatsJob
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
                    IsCritical = false // todo crit burn
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

    [BurstCompile]
    private partial struct CalculateFinalStatsJob : IJobEntity
    {
        [ReadOnly] public ComponentLookup<SlowEffect> SlowEffectLookup;

        // todo use other lookups if they have stats effects

        public void Execute([ChunkIndexInQuery] int chunkIndex, Entity entity, in CoreStats coreStats,
            ref FinalStats finalStats)
        {
            // todo implement

            float speedMultBonus = 0.0f;
            float dmgMultBonus = 1.0f;

            // Slow
            if (SlowEffectLookup.TryGetComponent(entity, out var slow) &&
                SlowEffectLookup.IsComponentEnabled(entity))
            {
                speedMultBonus -= slow.SpeedReductionMultiplier;
            }

            // Set Final Stats
            finalStats.MoveSpeed = coreStats.BaseMoveSpeed * (coreStats.MoveSpeedMultiplier + speedMultBonus);
            finalStats.GlobalDamageMultiplier = coreStats.GlobalDamageMultiplier * dmgMultBonus;
            // todo armor multiplier mais azy faut faire un calcul bizarre je pense un peu à la soulstone
        }
    }
}