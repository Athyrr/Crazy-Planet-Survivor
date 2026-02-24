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

    FireResistance = 6,
    IceResistance = 7,
    LightningResistance = 8,
    PoisonResistance = 9,
    LightResistance = 10,
    DarkResistance = 11,
    NatureResistance = 12,

    CooldownReduction = 13,
    AreaSize = 14,
    CollectRange = 15,
    BounceCount = 16,
    PierceCount = 17,
    CritChance = 18,
    CritMultiplier = 19,
    SizeMultiplier = 20,
}
