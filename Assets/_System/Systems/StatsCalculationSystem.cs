using Unity.Entities;
using UnityEngine;

public partial struct StatsCalculationSystem : ISystem
{

    public void OnUppate(ref SystemState state)
    {
        foreach (var (stats, baseStats, modifiers) in
            SystemAPI.Query<RefRW<Stats>, RefRO<BaseStats>, DynamicBuffer<StatModifier>>())
        {
            // Reset stats to base values
            stats.ValueRW.MaxHealth = baseStats.ValueRO.MaxHealth;
            stats.ValueRW.Speed = baseStats.ValueRO.Speed;
            stats.ValueRW.Damage = baseStats.ValueRO.Damage;
            stats.ValueRW.Armor = baseStats.ValueRO.Armor;
            stats.ValueRW.FireResistance = baseStats.ValueRO.FireResistance;
            stats.ValueRW.CooldownReduction = baseStats.ValueRO.CooldownReduction;
            stats.ValueRW.AreaSize = baseStats.ValueRO.AreaSize;
            stats.ValueRW.CollectRange = baseStats.ValueRO.CollectRange;

            // Apply all modifiers for each case
            foreach (StatModifier modifier in modifiers)
            {
                switch (modifier.Type)
                {
                    case EStatType.MaxHealth:
                        if (modifier.Strategy == EStatModiferStrategy.Flat)
                            stats.ValueRW.MaxHealth += modifier.Value;
                        else if (modifier.Strategy == EStatModiferStrategy.Multiply)
                            stats.ValueRW.MaxHealth *= modifier.Value;
                        break;

                    case EStatType.Speed:
                        if (modifier.Strategy == EStatModiferStrategy.Flat)
                            stats.ValueRW.Speed += modifier.Value;
                        else if (modifier.Strategy == EStatModiferStrategy.Multiply)
                            stats.ValueRW.Speed *= modifier.Value;
                        break;

                    case EStatType.Damage:
                        if (modifier.Strategy == EStatModiferStrategy.Flat)
                            stats.ValueRW.Damage += modifier.Value;
                        else if (modifier.Strategy == EStatModiferStrategy.Multiply)
                            stats.ValueRW.Damage *= modifier.Value;
                        break;

                    case EStatType.Armor:
                        if (modifier.Strategy == EStatModiferStrategy.Flat)
                            stats.ValueRW.Armor += modifier.Value;
                        else if (modifier.Strategy == EStatModiferStrategy.Multiply)
                            stats.ValueRW.Armor *= modifier.Value;
                        break;

                    case EStatType.FireResistance:
                        if (modifier.Strategy == EStatModiferStrategy.Flat)
                            stats.ValueRW.FireResistance += modifier.Value;
                        else if (modifier.Strategy == EStatModiferStrategy.Multiply)
                            stats.ValueRW.FireResistance *= modifier.Value;
                        break;

                    case EStatType.CooldownReduction:
                        if (modifier.Strategy == EStatModiferStrategy.Flat)
                            stats.ValueRW.CooldownReduction += modifier.Value;
                        else if (modifier.Strategy == EStatModiferStrategy.Multiply)
                            stats.ValueRW.CooldownReduction *= modifier.Value;
                        break;

                    case EStatType.AreaSize:
                        if (modifier.Strategy == EStatModiferStrategy.Flat)
                            stats.ValueRW.AreaSize += modifier.Value;
                        else if (modifier.Strategy == EStatModiferStrategy.Multiply)
                            stats.ValueRW.AreaSize *= modifier.Value;
                        break;

                    case EStatType.CollectRange:
                        if (modifier.Strategy == EStatModiferStrategy.Flat)
                            stats.ValueRW.CollectRange += modifier.Value;
                        else if (modifier.Strategy == EStatModiferStrategy.Multiply)
                            stats.ValueRW.CollectRange *= modifier.Value;
                        break;
                }

            }
        }

    }
}