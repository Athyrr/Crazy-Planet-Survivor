using Unity.Entities;

public struct OrbDatabaseBufferElement : IBufferElementData, System.IComparable<OrbDatabaseBufferElement>
{
    public Entity Prefab;
    public int Value;

    public int CompareTo(OrbDatabaseBufferElement other) => other.Value.CompareTo(Value);
}
