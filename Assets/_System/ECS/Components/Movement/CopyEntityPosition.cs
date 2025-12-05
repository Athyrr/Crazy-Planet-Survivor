using Unity.Mathematics;
using Unity.Entities;

public struct CopyEntityPosition : IComponentData
{
    public Entity Target;
    public float3 Offset;
}
