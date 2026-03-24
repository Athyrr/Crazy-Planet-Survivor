using Unity.Entities;

public struct RessourcesDatabaseBufferElement : IBufferElementData, System.IComparable<RessourcesDatabaseBufferElement>
{
    public Entity Prefab;
    public int Value;

    public int CompareTo(RessourcesDatabaseBufferElement other) => other.Value.CompareTo(Value);
}
