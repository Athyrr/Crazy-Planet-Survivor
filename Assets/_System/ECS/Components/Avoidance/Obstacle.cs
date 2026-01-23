using Unity.Entities;

public struct Obstacle : IComponentData
{
    public float AvoidanceRadius;
    public float Weight;
}
