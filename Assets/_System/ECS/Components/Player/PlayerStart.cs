using Unity.Entities;
using Unity.Mathematics;

public struct PlayerStart : IComponentData
{
    public float3 Position;
    public quaternion Rotation;
}

