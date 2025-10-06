using Unity.Entities;
using Unity.Mathematics;

public struct CameraWorldData : IComponentData
{
    public float3 Position;
    public float3 Forward;
    public float3 Right;
    public float3 Up;
}