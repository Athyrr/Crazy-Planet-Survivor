using Unity.Entities;

/// <summary>
/// Runtime state of the scene loader, owned exclusively by <c>SceneLoadingSystem</c>. Kept as a
/// singleton so the current/streaming scene entities have a single source of truth.
/// </summary>
public struct SceneLoadingState : IComponentData
{
    public ELoadPhase Phase;

    /// <summary>Scene currently on screen (Entity.Null when nothing is loaded).</summary>
    public Entity CurrentSceneEntity;

    /// <summary>Scene being streamed in during the <see cref="ELoadPhase.Loading"/> phase.</summary>
    public Entity LoadingSceneEntity;

    public EGameState TargetState;
    public bool SendStartRequest;

    public float Timer;
    public float MinScreenTime;
    public float Timeout;
    public float RunStartDelay;

    /// <summary>Normalized load progress [0,1] for the loading bar. Read by <c>LoadingProgressUI</c>.</summary>
    public float Progress;
}

public enum ELoadPhase : byte
{
    Idle,
    Loading,
}
