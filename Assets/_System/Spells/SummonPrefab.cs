using Unity.Entities;

/// <summary>Agent prefab of each Summon spell, on the spells-database entity, indexed like SpellPrefab (DB index).
/// Entity.Null for every non-Summon spell.</summary>
public struct SummonPrefab : IBufferElementData
{
    public Entity Prefab;
}
