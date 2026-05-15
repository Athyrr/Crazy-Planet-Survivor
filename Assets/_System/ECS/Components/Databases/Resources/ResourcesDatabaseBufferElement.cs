using _System.ECS.Authorings.Resources;
using Unity.Entities;

public struct ResourcesDatabaseBufferElement : IBufferElementData
{
    public Entity Prefab;
    public EResourceType Type;
}
