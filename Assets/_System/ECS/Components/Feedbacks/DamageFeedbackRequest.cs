using Unity.Entities;
using Unity.Mathematics;

public struct DamageFeedbackRequest : IComponentData
{
    public int Amount;
    public float3 Position;
}
