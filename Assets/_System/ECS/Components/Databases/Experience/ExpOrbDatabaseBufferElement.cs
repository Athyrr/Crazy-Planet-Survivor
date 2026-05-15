using Unity.Entities;

public struct ExpOrbDatabaseBufferElement : IBufferElementData, System.IComparable<ExpOrbDatabaseBufferElement>
{
    public Entity Prefab;
    public int Value;

    public int CompareTo(ExpOrbDatabaseBufferElement other) => other.Value.CompareTo(Value);
}
