using Unity.Entities;

/// <summary>
/// Buffer element to store hit entities by spells.
/// A spell with ¨Pierce¨ or ¨Ricochet¨ component can use this buffer to keep track of entities it has already hit,
/// </summary>
public struct HitEntityMemory : IBufferElementData
{
    public Entity HitEntity;
}