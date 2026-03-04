using Unity.Entities;
using Unity.Physics;

/// <summary>
/// Component that allows an entity to spawn child entities (ex: boulders, minions, blades etc.)
/// </summary>
public struct SubSpellsSpawner : IComponentData
{
    public Entity ChildEntityPrefab;
    public int DesiredSubSpellsCount;
    public CollisionFilter CollisionFilter;
    public bool IsDirty;
}

/// <summary>
/// Component that identifies a spell and its caster.
/// </summary>
public struct SpellSource : IComponentData
{
    public Entity CasterEntity;
    public int DatabaseIndex;
}