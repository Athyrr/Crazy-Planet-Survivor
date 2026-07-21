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
    Loading,
    AmuletShop,
    MetaProgression,

    // Entry state shown on launch (logo + New Game / Continue / Options) before any planet
    // content is streamed in. Appended last to keep existing enum values stable.
    MainMenu,

    // Intro window at the start of a run: the planet + player are visible but enemies and the run
    // timer are held until the RunStart delay elapses (arrival animation / intro text). Appended
    // last to keep existing enum values stable.
    RunStarting,
}