using Unity.Burst;
using Unity.Entities;

[BurstCompile]
public partial struct StatsCalculationSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<BaseStats>();
        state.RequireForUpdate<Stats>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton<GameState>(out var gameState))
            return;

        if (gameState.State != EGameState.Running)
            return;

        //@todo use RecalculateStatsRequest
        var job = new CalculateStatsJob();
        state.Dependency = job.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    private partial struct CalculateStatsJob : IJobEntity
    {
        public void Execute(ref Stats stats, in BaseStats baseStats, in DynamicBuffer<StatModifier> modifiers)
        {
            stats.MaxHealth = baseStats.MaxHealth;
            stats.Speed = baseStats.Speed;
            stats.Damage = baseStats.Damage;
            stats.Armor = baseStats.Armor;
            stats.FireResistance = baseStats.FireResistance;
            stats.CooldownReduction = baseStats.CooldownReduction;
            stats.AreaSize = baseStats.AreaSize;
            stats.CollectRange = baseStats.CollectRange;

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