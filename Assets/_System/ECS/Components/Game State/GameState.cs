using Unity.Entities;

/// <summary>
/// Singleton ECS component that tracks the state of the game.
/// </summary>
public struct GameState : IComponentData
{
    public EGameState State;
}

public enum EGameState
{
    Lobby,
    CharacterSelection,
    PlanetSelection,
    Running,
    Paused,
    GameOver,
    UpgradeSelection,
}