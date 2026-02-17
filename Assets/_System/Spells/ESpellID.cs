using System;

public enum ESpellID : byte
{
    None = 0,

    // Player Spells
    Fireball = 1,
    LightningStrike = 2,
    FrozenZone = 3,
    ShockChain = 4,
    TechHoles = 5,
    ShockBeam = 6,
    DraconicVomit = 7,
    LightningTornado = 8,
    PoisonFloor= 9,
    FireOrbs= 10,
    PoisonNeedle= 11,

    // Enemy Spells
    Enemy_Projectile = 12,

    VoidSlash = 13
}

/// <summary>
/// Wrapper struct to use ESpellID as key in NativeHashMap or NativeHashSet.
/// </summary>
public struct SpellKey : IEquatable<SpellKey>
{
    public ESpellID Value;

    public bool Equals(SpellKey other)
    {
        return Value == other.Value;
    }

    public override int GetHashCode()
    {
        return (int)Value;
    }
}