using Unity.Entities;

/// <summary>
/// Buffer element that represents a modifier to be applied spells sharing same tags.
/// This can be used for various purposes, such as temporary buffs, debuffs, or effects from items and upgrades.
/// </summary>
public struct SpellModifier : IBufferElementData
{
    public ESpellTag RequiredTags; 
    public ESpellStat SpellStat;  
    public float Value;            
    public EModiferStrategy Strategy;
}
