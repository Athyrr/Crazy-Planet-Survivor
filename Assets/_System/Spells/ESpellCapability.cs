using System;

/// <summary>
/// What a spell is ALLOWED to do (the SpellSO decides, the prefab only offers the slot).
/// Mirrors the Bounce / Pierce / ExplodeOnContact enableable components.
/// </summary>
[Flags]
public enum ESpellCapability : byte
{
    None = 0,
    Bounce = 1 << 0,
    Pierce = 1 << 1,
    Explode = 1 << 2,
}
