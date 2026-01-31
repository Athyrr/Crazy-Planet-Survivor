using Unity.Entities;

/// <summary>
/// Represents a buffer element for a character spell pool available in a run.
/// </summary>
public struct SpellPoolBufferElement : IBufferElementData
{
    public int DatabaseIndex;
    public int Rarity;
}
