using Unity.Entities;

/// <summary>
/// Distance an entity keeps from its goal (the player). Beyond <see cref="Distance"/> it advances;
/// within it, it holds position. If <see cref="RetreatDistance"/> &gt; 0 and the player gets closer than
/// that, the entity backs away — turning a plain stopper into a ranged kiter.
/// </summary>
public struct StopDistance : IComponentData
{
    /// <summary> Range the entity holds: it stops advancing once the player is within this. </summary>
    public float Distance;

    /// <summary>
    /// Range below which the entity retreats from the player (0 = never retreats, plain stop-and-hold).
    /// Keep it below <see cref="Distance"/>; the gap between the two is the comfort band it settles in.
    /// </summary>
    public float RetreatDistance;
}
