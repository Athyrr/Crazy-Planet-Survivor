using Unity.Entities;
using Unity.Physics;

/// <summary>
/// Component that allows an entity to spawn child entities (ex: boulders, minions, blades etc.)
/// </summary>
public struct ChildEntitiesSpawner : IComponentData
{
    public Entity ChildEntityPrefab;
    public int DesiredChildrenCount;
    public CollisionFilter CollisionFilter;
    public bool IsDirty;
}

/// <summary>
/// Represents a buffer element that stores a reference to a child entity.
/// </summary>
public struct LinkedChildEntityBufferElement : IBufferElementData
{
    //@todo chgeck if just CHild is enough
    public Entity ChildEntity;
}