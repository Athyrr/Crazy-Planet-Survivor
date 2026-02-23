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
/// Component added to the root entity of a spell (ex the center of fire orbs).
/// Allows this entity to be linked to the player's data (ActiveSpell) for upgrades.
/// </summary>
public struct SpellLink : IComponentData
{
    public Entity CasterEntity;
    public int DatabaseIndex;
}