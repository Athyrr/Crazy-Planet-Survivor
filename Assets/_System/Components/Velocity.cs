using Unity.Entities;
using Unity.Mathematics;

public struct Velocity : IComponentData
{
    public float3 Direction;
    public float Magnitude;
}

public struct RotationSpeed : IComponentData
{
   public float Value;
}

public struct SpawnConfig : IComponentData
{
    public Entity Prefab;
    public int Amount;
    public float Range;
}

public struct PlanetData : IComponentData
{
    public Entity Prefab;
    public float Radius;
}
