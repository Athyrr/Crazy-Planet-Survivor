using Unity.Entities;
using Unity.Mathematics;

/// <summary>World-space segment of a Capsule zone, written every frame by its owner system (BeamAimSystem). Present →
/// AreaAttackSystem's OverTime Capsule spans A→B instead of entity origin → Offset point. SurfaceArc → exact angular
/// test along the great circle A→B on the sphere centred on Center (spec channeled-beam, decision I4); without it,
/// the straight capsule A→B applies.</summary>
public struct AreaSegment : IComponentData
{
    public float3 A;
    public float3 B;
    public float3 Center;
    public bool SurfaceArc;
}
