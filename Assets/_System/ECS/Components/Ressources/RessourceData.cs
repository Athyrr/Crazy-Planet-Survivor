using _System.ECS.Authorings.Ressources;
using Unity.Entities;

public struct Resource : IComponentData
{
    public int Value;
    public ERessourceType Type;
}