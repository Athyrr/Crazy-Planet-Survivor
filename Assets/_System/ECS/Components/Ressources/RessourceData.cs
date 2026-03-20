using _System.ECS.Authorings.Ressources;
using Unity.Entities;

public struct Ressource : IComponentData
{
    public float Value;
    public ERessourceType Type;
    public bool Persistant;
}