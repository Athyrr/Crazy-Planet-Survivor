using Unity.Burst;
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

        _calculateQuery = SystemAPI.QueryBuilder()
            .WithAll<RecalculateStatsRequest, Stats, BaseStats, StatModifier>()
            .Build();

        state.RequireForUpdate(_calculateQuery); 
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton<GameState>(out var gameState))
            return;

        if (gameState.State != EGameState.Running)
            return;

        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        var job = new CalculateStatsJob()
        {
            ECB = ecb.AsParallelWriter()
        };
        state.Dependency = job.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    private partial struct CalculateStatsJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter ECB;

        public void Execute([ChunkIndexInQuery] int index, Entity entity, in RecalculateStatsRequest recalculateStatsRequest, ref Stats stats, ref Health health, in BaseStats baseStats, in DynamicBuffer<StatModifier> modifiers)
        {
            stats.MaxHealth = baseStats.MaxHealth;
            stats.Speed = baseStats.Speed;
            stats.Damage = baseStats.Damage;
            stats.Armor = baseStats.Armor;
            stats.FireResistance = baseStats.FireResistance;
            stats.CooldownReduction = baseStats.CooldownReduction;
            stats.AreaSize = baseStats.AreaSize;
            stats.CollectRange = baseStats.CollectRange;


            float oldMaxHealth = stats.MaxHealth > 0 ? stats.MaxHealth : baseStats.MaxHealth; // Avoid division by zero
            if (oldMaxHealth <= 0) oldMaxHealth = 1;


            for (var i = 0; i < modifiers.Length; i++)
            {
                switch (modifiers[i].Type)
                {
                    case EStatType.MaxHealth:
                        StatsCalculationSystem.ApplyModifier(ref stats.MaxHealth, modifiers[i]);
                        break;

                    case EStatType.Speed:
                        ApplyModifier(ref stats.Speed, modifiers[i]);
                        break;

                    case EStatType.Damage:
                        ApplyModifier(ref stats.Damage, modifiers[i]);
                        break;

                    case EStatType.Armor:
                        ApplyModifier(ref stats.Armor, modifiers[i]);
                        break;

                    case EStatType.FireResistance:
                        ApplyModifier(ref stats.FireResistance, modifiers[i]);
                        break;

                    case EStatType.CooldownReduction:
                        ApplyModifier(ref stats.CooldownReduction, modifiers[i]);
                        break;

                    case EStatType.AreaSize:
                        ApplyModifier(ref stats.AreaSize, modifiers[i]);
                        break;

                    case EStatType.CollectRange:
                        ApplyModifier(ref stats.CollectRange, modifiers[i]);
                        break;
                }
            }

            if (stats.MaxHealth != oldMaxHealth && stats.MaxHealth > 0)
            {
                float healthRatio = health.Value / oldMaxHealth;
                health.Value = healthRatio * stats.MaxHealth;
            }
            // Ensure health doesn't exceed new max
            health.Value = math.min(health.Value, stats.MaxHealth);

            // remove RecalculateStatsRequest
            ECB.RemoveComponent<RecalculateStatsRequest>(index, entity);
        }
    }

    private static void ApplyModifier(ref float statValue, in StatModifier modifier)
    {
        if (modifier.Strategy == EStatModiferStrategy.Flat)
            statValue += modifier.Value;
        else if (modifier.Strategy == EStatModiferStrategy.Multiply)
            statValue *= modifier.Value;
    }
}