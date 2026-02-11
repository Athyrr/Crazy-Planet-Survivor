using Unity.Entities;

/// <summary>
/// Request when a run ends by death or timer and wait for GameOver UI display.
/// </summary>
public struct EndRunRequest : IComponentData
{
    public EEndRunState State;
}

public enum EEndRunState
{
    Death,
    Timeout
}