using _System.ECS.Authorings.Ressources;
using Unity.Entities;

public struct CollectedRessourcesBufferElement : IBufferElementData
{
    public ERessourceType Type;
    public int Value;
}
