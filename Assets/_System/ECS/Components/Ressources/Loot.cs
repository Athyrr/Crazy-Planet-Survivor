using Unity.Entities;

public struct Loot : IComponentData
{
    public int Value;
    public float DropChance;
}
