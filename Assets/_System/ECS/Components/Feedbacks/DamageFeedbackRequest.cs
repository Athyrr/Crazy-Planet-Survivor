using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

public struct DamageFeedbackRequest : IComponentData
{
    public int Amount;
    public LocalTransform Transform;
    public bool IsCrit;
}
