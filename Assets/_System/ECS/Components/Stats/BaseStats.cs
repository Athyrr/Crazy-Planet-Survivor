using Unity.Entities;

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

