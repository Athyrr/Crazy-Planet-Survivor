using Unity.Entities;
using Unity.Mathematics;

public struct LinearMovement : IComponentData
{
    public float3 Direction;
    public float Speed;
}
