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
                        if (modifiers[i].Strategy == EStatModiferStrategy.Flat)
                            stats.MaxHealth += modifiers[i].Value;
                        else if (modifiers[i].Strategy == EStatModiferStrategy.Multiply)
                            stats.MaxHealth *= modifiers[i].Value;
                        break;

                    case EStatType.Speed:
                        if (modifiers[i].Strategy == EStatModiferStrategy.Flat)
                            stats.Speed += modifiers[i].Value;
                        else if (modifiers[i].Strategy == EStatModiferStrategy.Multiply)
                            stats.Speed *= modifiers[i].Value;
                        break;

                    case EStatType.Damage:
                        if (modifiers[i].Strategy == EStatModiferStrategy.Flat)
                            stats.Damage += modifiers[i].Value;
                        else if (modifiers[i].Strategy == EStatModiferStrategy.Multiply)
                            stats.Damage *= modifiers[i].Value;
                        break;

                    case EStatType.Armor:
                        if (modifiers[i].Strategy == EStatModiferStrategy.Flat)
                            stats.Armor += modifiers[i].Value;
                        else if (modifiers[i].Strategy == EStatModiferStrategy.Multiply)
                            stats.Armor *= modifiers[i].Value;
                        break;

                    case EStatType.FireResistance:
                        if (modifiers[i].Strategy == EStatModiferStrategy.Flat)
                            stats.FireResistance += modifiers[i].Value;
                        else if (modifiers[i].Strategy == EStatModiferStrategy.Multiply)
                            stats.FireResistance *= modifiers[i].Value;
                        break;

                    case EStatType.CooldownReduction:
                        if (modifiers[i].Strategy == EStatModiferStrategy.Flat)
                            stats.CooldownReduction += modifiers[i].Value;
                        else if (modifiers[i].Strategy == EStatModiferStrategy.Multiply)
                            stats.CooldownReduction *= modifiers[i].Value;
                        break;

                    case EStatType.AreaSize:
                        if (modifiers[i].Strategy == EStatModiferStrategy.Flat)
                            stats.AreaSize += modifiers[i].Value;
                        else if (modifiers[i].Strategy == EStatModiferStrategy.Multiply)
                            stats.AreaSize *= modifiers[i].Value;
                        break;

                    case EStatType.CollectRange:
                        if (modifiers[i].Strategy == EStatModiferStrategy.Flat)
                            stats.CollectRange += modifiers[i].Value;
                        else if (modifiers[i].Strategy == EStatModiferStrategy.Multiply)
                            stats.CollectRange *= modifiers[i].Value;
                        break;
                }

            }
        }
    }
}