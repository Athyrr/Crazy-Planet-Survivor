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
    
    CooldownReduction = 13,
    AreaSize = 14,
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
}
