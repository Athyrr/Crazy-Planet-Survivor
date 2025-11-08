using Unity.Entities;

/// <summary>
/// Component data for spell that bounce between mulitple targets.
/// </summary>
public struct RicochetData : IComponentData
{
    public int RemainingBounces;
    public float SearchRadius;
    public float Speed;
}