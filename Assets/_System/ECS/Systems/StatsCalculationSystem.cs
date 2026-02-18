using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

[BurstCompile]
public partial struct StatsCalculationSystem : ISystem
{
    private EntityQuery _calculateQuery;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<BaseStats>();
        state.RequireForUpdate<Stats>();

        _calculateQuery = SystemAPI
            .QueryBuilder()
            .WithAll<RecalculateStatsRequest, Stats, BaseStats, StatModifier>()
            .Build();

        state.RequireForUpdate(_calculateQuery);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton<GameState>(out var gameState))
            return;

        //if (gameState.State != EGameState.Running)
        //    return;

        var ecbSingleton =
            SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        var job = new CalculateStatsJob() { ECB = ecb.AsParallelWriter() };
        state.Dependency = job.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    private partial struct CalculateStatsJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter ECB;

        public void Execute(
            [ChunkIndexInQuery] int index,
            Entity entity,
            in RecalculateStatsRequest recalculateStatsRequest,
            ref Stats stats,
            ref Health health,
            in BaseStats baseStats,
            in DynamicBuffer<StatModifier> modifiers
        )
        {
            // Reset to base
            stats.MaxHealth = baseStats.MaxHealth;
            stats.Armor = baseStats.Armor;
            stats.MoveSpeed = baseStats.MoveSpeed;
            stats.Damage = baseStats.Damage;
            stats.CooldownReduction = baseStats.CooldownReduction;
            stats.ProjectileSpeedMultiplier = baseStats.ProjectileSpeedMultiplier;
            stats.EffectAreaRadiusMult = baseStats.EffectAreaRadiusMultiplier;
            stats.BouncesAdded = baseStats.BouncesAdded;
            stats.PierceAdded = baseStats.PierceAdded;
            stats.FireResistance = baseStats.FireResistance;
            stats.IceResistance = baseStats.IceResistance;
            stats.LightningResistance = baseStats.LightningResistance;
            stats.ArcaneResistance = baseStats.ArcaneResistance;
            stats.CollectRange = baseStats.CollectRange;
            stats.MaxCollectRange = baseStats.MaxCollectRange;

            float oldMaxHealth = stats.MaxHealth > 0 ? stats.MaxHealth : baseStats.MaxHealth;
            if (oldMaxHealth <= 0)
                oldMaxHealth = 1;

            // Separate flat and additive multipliers
            // We use a simple array to store multipliers since we only have a few stats
            // Index corresponds to ECharacterStat enum
            NativeArray<float> additiveMultipliers = new NativeArray<float>(20, Allocator.Temp);
            NativeArray<float> flatModifiers = new NativeArray<float>(20, Allocator.Temp);

            for (var i = 0; i < modifiers.Length; i++)
            {
                int statIndex = (int)modifiers[i].StatID;
                if (modifiers[i].Strategy == EStatModiferStrategy.Multiply)
                {
                    // For additive multipliers, 1.1 (+10%) becomes 0.1
                    additiveMultipliers[statIndex] += (modifiers[i].Value - 1.0f);
                }
                else
                {
                    flatModifiers[statIndex] += modifiers[i].Value;
                }
            }

            // Apply modifiers
            ApplyAllModifiers(
                ref stats.MaxHealth,
                ECharacterStat.MaxHealth,
                additiveMultipliers,
                flatModifiers
            );
            ApplyAllModifiers(
                ref stats.Armor,
                ECharacterStat.Armor,
                additiveMultipliers,
                flatModifiers
            );
            ApplyAllModifiers(
                ref stats.MoveSpeed,
                ECharacterStat.Speed,
                additiveMultipliers,
                flatModifiers
            );
            ApplyAllModifiers(
                ref stats.Damage,
                ECharacterStat.Damage,
                additiveMultipliers,
                flatModifiers
            );
            ApplyAllModifiers(
                ref stats.CooldownReduction,
                ECharacterStat.CooldownReduction,
                additiveMultipliers,
                flatModifiers
            );
            ApplyAllModifiers(
                ref stats.ProjectileSpeedMultiplier,
                ECharacterStat.ProjectileSpeed,
                additiveMultipliers,
                flatModifiers
            );
            ApplyAllModifiers(
                ref stats.EffectAreaRadiusMult,
                ECharacterStat.AreaSize,
                additiveMultipliers,
                flatModifiers
            );

            // Int stats
            float bounceF = (float)stats.BouncesAdded;
            ApplyAllModifiers(
                ref bounceF,
                ECharacterStat.BounceCount,
                additiveMultipliers,
                flatModifiers
            );
            stats.BouncesAdded = (int)bounceF;

            float pierceF = (float)stats.PierceAdded;
            ApplyAllModifiers(
                ref pierceF,
                ECharacterStat.PierceCount,
                additiveMultipliers,
                flatModifiers
            );
            stats.PierceAdded = (int)pierceF;

            ApplyAllModifiers(
                ref stats.FireResistance,
                ECharacterStat.FireResistance,
                additiveMultipliers,
                flatModifiers
            );
            ApplyAllModifiers(
                ref stats.IceResistance,
                ECharacterStat.IceResistance,
                additiveMultipliers,
                flatModifiers
            );
            ApplyAllModifiers(
                ref stats.LightningResistance,
                ECharacterStat.LightningResistance,
                additiveMultipliers,
                flatModifiers
            );
            ApplyAllModifiers(
                ref stats.ArcaneResistance,
                ECharacterStat.ArcaneResistance,
                additiveMultipliers,
                flatModifiers
            );

            ApplyAllModifiers(
                ref stats.CollectRange,
                ECharacterStat.CollectRange,
                additiveMultipliers,
                flatModifiers
            );
            ApplyAllModifiers(
                ref stats.MaxCollectRange,
                ECharacterStat.MaxCollectRange,
                additiveMultipliers,
                flatModifiers
            );

            // Cap collect range
            stats.CollectRange = math.min(stats.CollectRange, stats.MaxCollectRange);

            if (stats.MaxHealth != oldMaxHealth && stats.MaxHealth > 0)
            {
                float healthRatio = health.Value / oldMaxHealth;
                health.Value = healthRatio * stats.MaxHealth;
            }
            health.Value = math.min(health.Value, stats.MaxHealth);

            ECB.RemoveComponent<RecalculateStatsRequest>(index, entity);

            additiveMultipliers.Dispose();
            flatModifiers.Dispose();
        }

        private void ApplyAllModifiers(
            ref float baseValue,
            ECharacterStat stat,
            NativeArray<float> adds,
            NativeArray<float> flats
        )
        {
            int idx = (int)stat;
            baseValue = baseValue * (1.0f + adds[idx]) + flats[idx];
        }
    }
}
