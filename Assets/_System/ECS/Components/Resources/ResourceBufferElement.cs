using _System.ECS.Authorings.Resources;
using Unity.Entities;

public struct ResourceBufferElement : IBufferElementData
{
    public EResourceType Type;
    public int Value;
}