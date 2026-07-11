using Unity.Entities;

/// <summary>
/// One-shot request to stream in a planet scene. Emitted by <c>GameManager</c> and consumed by
/// <c>SceneLoadingSystem</c>, which owns the whole unload/stream/transition sequence.
/// </summary>
public struct LoadSceneRequest : IComponentData
{
    public EPlanetID PlanetID;

    /// <summary>State the game enters once the scene is fully streamed and its data is ready.</summary>
    public EGameState TargetState;

    /// <summary>When true, a <see cref="StartRunRequest"/> is raised after the transition.</summary>
    public bool SendStartRequest;

    /// <summary>Minimum time the loading screen stays up (cosmetic floor, avoids flashes).</summary>
    public float MinScreenTime;

    /// <summary>Hard cap (seconds) before a stuck load is aborted instead of hanging forever.</summary>
    public float Timeout;

    /// <summary>
    /// Intro delay (seconds) held after the loading screen is hidden before the run actually starts.
    /// Only applies when <see cref="SendStartRequest"/> is true; 0 starts the run immediately.
    /// </summary>
    public float RunStartDelay;
}
