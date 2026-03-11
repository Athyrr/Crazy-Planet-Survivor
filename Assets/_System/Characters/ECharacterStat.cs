/// <summary>
/// All possible character stats labels. Make sure that <see cref="ECharacterStat"/> elements correspond to <see cref="Stats"/> + <see cref="BaseStats"/> elements.
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
    
}
