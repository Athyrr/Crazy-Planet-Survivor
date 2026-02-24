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
    Poison = 1 << 4,
    Light = 1 << 5,
    Dark = 1 << 6,
    Nature = 1 << 7,

    // Types
    Projectile = 1 << 8,
    Area = 1 << 9,
    Summon = 1 << 10,
    Buff = 1 << 11,
    Debuff = 1 << 12,

    // Behaviors
    Explosive = 1 << 13,
    Slashing = 1 << 14,
    Crushing = 1 << 15,
    Piercing = 1 << 16,
    Bouncing = 1 << 17,

    MagicDamage = Fire | Ice | Lightning | Light | Dark | Nature,
    All = ~0u,
}
