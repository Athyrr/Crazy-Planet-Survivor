using Unity.Entities;
using Unity.Mathematics;

public struct PlanetData : IComponentData
{
    public EPlanetID PlanetID;

    public Entity Prefab;

    public float3 Center;

    public float Radius;

    public float RunDuration;
}
