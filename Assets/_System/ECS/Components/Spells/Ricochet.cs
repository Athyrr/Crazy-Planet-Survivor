using Unity.Entities;

/// <summary>
/// Component data for spell that bounce between mulitple targets.
/// </summary>
public struct Ricochet : IComponentData
{
    /// <summary>
    /// Remaining bounces.
    /// </summary>
    public int RemainingBounces;

    /// <summary>
    /// Search range for next bounce target.
    /// </summary>
    public float BounceRange;

    public float BounceSpeed;
}