using Unity.Entities;

public struct PlanetData : IComponentData
{
    public Entity Prefab;
    public float Radius;
}
