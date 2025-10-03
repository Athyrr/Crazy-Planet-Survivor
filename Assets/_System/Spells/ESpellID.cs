using System;

public enum ESpellID : byte
{
    None,
    Fireball,
    LightningStrike,
    FrozenZone
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