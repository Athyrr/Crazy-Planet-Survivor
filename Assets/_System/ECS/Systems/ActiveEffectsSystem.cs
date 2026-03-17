using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(EntitiesMovementSystem))]
[BurstCompile]
public partial struct ActiveEffectsSystem : ISystem
{
    private ComponentLookup<SlowEffect> _slowEffectLookup;
    private ComponentLookup<StunEffect> _stunEffectLookup;
    private ComponentLookup<BurnEffect> _burnEffectLookup;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();

        // todo job for other effects
        _slowEffectLookup = state.GetComponentLookup<SlowEffect>();
        _stunEffectLookup = state.GetComponentLookup<StunEffect>();
        _burnEffectLookup = state.GetComponentLookup<BurnEffect>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter();

        var dt = SystemAPI.Time.DeltaTime;

        // Update lookups
        _slowEffectLookup.Update(ref state);
        _stunEffectLookup.Update(ref state);
        _burnEffectLookup.Update(ref state);

        var processBurnJob = new BurnJob
        {
            DeltaTime = dt,
            ECB = ecb,

            BurnEffectLookup = _burnEffectLookup
        };
        processBurnJob.ScheduleParallel();

        // todo job for other effects

        var calculateCurrentStatsJob = new CalculateFinalStatsJob
        {
            DeltaTime = dt,
            ECB = ecb,

            SlowEffectLookup = _slowEffectLookup,
            StunEffectLookup = _stunEffectLookup,
        };
        calculateCurrentStatsJob.ScheduleParallel();
    }

    [BurstCompile]
    [WithAll(typeof(BurnEffect))]
    private partial struct BurnJob : IJobEntity
    {
        public float DeltaTime;
        public EntityCommandBuffer.ParallelWriter ECB;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<BurnEffect> BurnEffectLookup;

        private void Execute([ChunkIndexInQuery] int chunkIndex, Entity entity)
        {
            if (!BurnEffectLookup.IsComponentEnabled(entity))
                return;

            var burn = BurnEffectLookup[entity];

            burn.RemainingTime -= DeltaTime;

            if (burn.RemainingTime <= 0)
            {
                ECB.SetComponentEnabled<BurnEffect>(chunkIndex, entity, false);
                return;
            }

            burn.TickTimer += DeltaTime;
            if (burn.TickTimer >= burn.TickRate)
            {
                burn.TickTimer -= burn.TickRate;

                if (entity == Entity.Null)
                    return;

                ECB.AppendToBuffer(chunkIndex, entity, new DamageBufferElement()
                {
                    Damage = (int)burn.DamageOnTick,
                    Tag = ESpellTag.Burn,
                    IsCritical = false // todo crit burn
                });
            }

            BurnEffectLookup[entity] = burn;
        }
    }

    [BurstCompile]
    private partial struct CalculateFinalStatsJob : IJobEntity
    {
        public float DeltaTime;
        public EntityCommandBuffer.ParallelWriter ECB;

        [ReadOnly] public ComponentLookup<SlowEffect> SlowEffectLookup;
        [ReadOnly] public ComponentLookup<StunEffect> StunEffectLookup;

        // todo use other lookups if they have stats effects

        public void Execute([ChunkIndexInQuery] int chunkIndex, Entity entity, in CoreStats coreStats,
            ref FinalStats finalStats)
        {
            // todo implement

            float speedMultBonus = 1.0f;
            float dmgMultBonus = 1.0f;

            // Slow
            if (SlowEffectLookup.TryGetComponent(entity, out var slow) && SlowEffectLookup.IsComponentEnabled(entity))
            {
                slow.DurationLeft -= DeltaTime;

                if (slow.DurationLeft <= 0)
                {
                    SlowEffectLookup.SetComponentEnabled(entity, false);
                }
                else
                {
                    speedMultBonus -= slow.SpeedReductionMultiplier;
                }

                ECB.SetComponent(chunkIndex, entity, slow);
            }

            // Stun
            if (StunEffectLookup.TryGetComponent(entity, out var stun) && StunEffectLookup.IsComponentEnabled(entity))
            {
                stun.DurationLeft -= DeltaTime;

                if (stun.DurationLeft <= 0)
                {
                    StunEffectLookup.SetComponentEnabled(entity, false);
                }

                ECB.SetComponent(chunkIndex, entity, stun);
            }

            // Set CurrentStats
            finalStats.MoveSpeed = coreStats.BaseMoveSpeed * (coreStats.MoveSpeedMultiplier + speedMultBonus);
            finalStats.GlobalDamageMultiplier = coreStats.GlobalDamageMultiplier * dmgMultBonus;
            // todo armor multiplier mais azy faut faire un calcul bizarre je pense un peu à la soulstone
        }
    }
}