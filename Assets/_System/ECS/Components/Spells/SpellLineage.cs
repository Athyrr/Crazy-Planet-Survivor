using Unity.Entities;

/// <summary>Cascade bookkeeping of an emitting/emitted spell instance. Only Split increments Generation and
/// decrements DepthLeft. LineageId = first instance of the original cast (shared by multishot, explosions and
/// fragments) — key of the per-lineage spawn budget.</summary>
public struct SpellLineage : IComponentData
{
    public Entity LineageId;
    public byte Generation;
    public byte DepthLeft;
    /// <summary>DB index of the SpellSO this instance IS (its FORM): its Emitters[] are evaluated on impact.</summary>
    public int ShapeDatabaseIndex;
}
