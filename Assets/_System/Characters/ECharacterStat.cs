/// <summary>
/// All possible character stats labels. Make sure that <see cref="ECharacterStat"/> elements correspond to <see cref="CoreStats"/> + <see cref="BaseStats"/> elements.
/// </summary>
public enum ECharacterStat
{
    None = 0,
    MaxHealth = 1,
    Health = 2,
    Speed = 3,
    Damage = 4,
    Armor = 5,
    
    AttackSpeed = 13,
    SizeMultiplier = 20,
    
    CollectRange = 15,
    
    BounceCount = 16,
    PierceCount = 17,
    
    CritChance = 18,
    CritDamage = 19,

    SpellSpeed = 21,

    BurnDamage = 22,
    BurnDuration = 23,
    SlowStrength = 24,
    SlowDuration = 25,
    StunDuration = 26,

    HealthRegen = 27,

    SpellDuration = 28,
    CastRange = 29,
    Amount = 30,

    /// <summary>Increases the chance of drawing rarer stat upgrades on level up.</summary>
    Luck = 31,
}
