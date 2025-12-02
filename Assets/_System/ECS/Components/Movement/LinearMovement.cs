using Unity.Entities;
using Unity.Mathematics;

public struct LinearMovement : IComponentData, IEnableableComponent
{
    public float3 Direction;
    public float Speed;
}
