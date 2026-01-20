using Unity.Entities;

/// <summary>
/// Buffer of all Spells Upgrades for an character.
/// </summary>
public struct SpellsUpgradePoolBufferElement : IBufferElementData
{
    public int DatabaseIndex;
}