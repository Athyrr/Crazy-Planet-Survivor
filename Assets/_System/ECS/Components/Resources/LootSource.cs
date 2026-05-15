using _System.ECS.Authorings.Resources;
using Unity.Entities;

public struct LootSource : IComponentData
{
    public EResourceType Type;
    public int Value;
    public float DropChance;
    public bool IsExperience;
}
