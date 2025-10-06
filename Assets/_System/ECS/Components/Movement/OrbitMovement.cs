using Unity.Entities;
using Unity.Mathematics;

public struct OrbitMovement : IComponentData
{
    public float3 OrbitCenter;
    public float AngularSpeed;
    public float Radius;
}
