using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Represents a steering force applied to an entity for avoidance behavior.
/// </summary>
public struct SteeringForce : IComponentData
{
    public float3 Value;
}
