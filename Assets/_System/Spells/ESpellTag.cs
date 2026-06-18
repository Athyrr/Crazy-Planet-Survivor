using System;

[Flags]
public enum ESpellTag : uint
{
    None = 0,

    // Delivery / range — exclusive per offensive spell (where it hits)
    Ranged = 1 << 22,
    Melee = 1 << 23,

    // Form / mechanic (what the spell is)
    Projectile = 1 << 8,
    Area = 1 << 9,
    Summon = 1 << 10,
    Buff = 1 << 11,
    Debuff = 1 << 12,

    // Behaviors
    Explosive = 1 << 13,
    Piercing = 1 << 16,
    Bouncing = 1 << 17,

    // Active Effects
    Burn = 1 << 18,
    Stun = 1 << 19,
    Slow = 1 << 20,
    Knockback = 1 << 21,

    // bits 0-7, 14, 15 intentionally free (former Elements / Slashing / Crushing)
    All = ~0u,
}
