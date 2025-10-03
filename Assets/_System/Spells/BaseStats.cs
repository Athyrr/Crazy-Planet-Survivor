using Unity.Entities;

/// <summary>
/// Represents all stats an entity has during a run. Make sure that <see cref="Stats"/> elements correspond to <see cref="EStatType"/> + <see cref="BaseStats"/> elements.
/// </summary>
public struct Stats : IComponentData
{
    public float MaxHealth;
    public float Speed;
    public float Damage;
    public float Armor;
    public float FireResistance;
    public float CooldownReduction;
    public float AreaSize;
    public float CollectRange;

    // @todo all stats available in game
    // Same as BaseStats 
}

/// <summary>
/// Represents base stats of an entity.
/// Make sure that <see cref="BaseStats"/> elements correspond to <see cref="EStatType"/> + <see cref="Stats"/> elements .
/// </summary>
[System.Serializable]
public struct BaseStats : IComponentData
{
    public float MaxHealth;
    public float Speed;
    public float Damage;
    public float Armor;
    public float FireResistance;
    public float CooldownReduction;
    public float AreaSize;
    public float CollectRange;
    // etc
}


[System.Serializable]
public struct StatModifier : IBufferElementData
{
    public EStatType Type;
    public float Value;
    public EStatModiferStrategy Strategy;

}

