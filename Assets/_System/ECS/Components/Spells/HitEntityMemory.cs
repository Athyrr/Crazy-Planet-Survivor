using Unity.Collections;
using Unity.Entities;

/// <summary>
/// Buffer element to store hit entities by spells.
/// A spell with Pierce or Ricochet component can use this buffer to keep track of entities it has already hit,
/// </summary>
public struct HitEntityMemory : IBufferElementData
{
    public Entity HitEntity;
    public double LastHitTime;
    /// <summary>Pre-filled entry (the target that triggered an emission): never re-hit, never evicted.</summary>
    public bool Pinned;
}