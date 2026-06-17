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
    Nature = 1 << 7,
    // bits 5,6 free (former Light / Dark)

    // Types
    Projectile = 1 << 8,
    Area = 1 << 9,
    Summon = 1 << 10,
    Buff = 1 << 11,
    Debuff = 1 << 12,
    Ranged = 1 << 22,
    Melee = 1 << 23,

    // Behaviors
    Explosive = 1 << 13,
    Piercing = 1 << 16,
    Bouncing = 1 << 17,
    // bits 14,15 free (former Slashing / Crushing)

    // Active Effects
    Burn = 1 << 18,
    Stun = 1 << 19,
    Slow = 1 << 20,
    Knockback = 1 << 21,

    All = ~0u,
}
