using _System.ECS.Authorings.Ressources;
using Unity.Entities;

public struct Loot : IComponentData
{
    public ERessourceType Type;
    public int Value;
    public float DropChance;
}
