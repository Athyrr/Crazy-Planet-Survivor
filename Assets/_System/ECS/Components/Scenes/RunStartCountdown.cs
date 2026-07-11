using Unity.Entities;

/// <summary>
/// Active during the <see cref="EGameState.RunStarting"/> intro window. Ticks down while the planet
/// and player are on screen; when it reaches zero the run truly begins (<see cref="TargetState"/>).
/// Owned by <c>RunStartSystem</c>.
/// </summary>
public struct RunStartCountdown : IComponentData
{
    public float Remaining;

    /// <summary>State entered once the intro delay elapses (normally <see cref="EGameState.Running"/>).</summary>
    public EGameState TargetState;
}
