using Unity.Entities;
using Unity.Mathematics;

public struct InputData : IComponentData
{
    public float2 Value;

    public bool IsInteractPressed;
}
