using _System.ECS.Authorings.Ressources;
using Unity.Entities;

public struct Ressource : IComponentData
{
    public int Value;
    public ERessourceType Type;
}