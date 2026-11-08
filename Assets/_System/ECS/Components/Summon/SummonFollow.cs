using Unity.Entities;
using Unity.Mathematics;

/// <summary>Locomotion state of a Follow Summon (pet): persistent Reynolds velocity (same model as
/// FlowFieldFollowerMovement) and the leash hysteresis. Present on every agent, read only for Mobility Follow.</summary>
public struct SummonFollow : IComponentData
{
    public float3 Velocity;
    public bool Returning;
}
