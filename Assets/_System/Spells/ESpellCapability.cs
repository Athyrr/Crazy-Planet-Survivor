using System;

/// <summary>
/// What a spell is ALLOWED to do (the SpellSO decides, the prefab only offers the slot).
/// Bounce / Pierce mirror the enableable components of the same name; Explode / Split / Emit mirror an emitter
/// entry of that capability (Split active while FinalSplitCount > 0; Explode and Emit active once granted).
/// </summary>
[Flags]
public enum ESpellCapability : byte
{
    None = 0,
    Bounce = 1 << 0,
    Pierce = 1 << 1,
    Explode = 1 << 2,
    Split = 1 << 3,
    Emit = 1 << 4,
}
