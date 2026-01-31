using System;

[Flags]
public enum ESpellTag : uint 
{
    None = 0,

    // Elements
    Physical = 1 << 0, 
    Fire = 1 << 1,  
    Ice = 1 << 2, 
    Lightning = 1 << 3,  
    Arcane = 1 << 4,  
    Poison = 1 << 5, 
    Holy = 1 << 6,  
    Dark = 1 << 7, 

    // Types 
    Projectile = 1 << 8, 
    Area = 1 << 9,  
    Summon = 1 << 10, 
    Buff = 1 << 11, 
    Debuff = 1 << 12,

    // Behaviors
    Explosive = 1 << 13, 
    Piercing = 1 << 14,
    Bouncing = 1 << 15,

    MagicDamage = Fire | Ice | Lightning | Arcane | Holy | Dark,
    All = ~0u 
}