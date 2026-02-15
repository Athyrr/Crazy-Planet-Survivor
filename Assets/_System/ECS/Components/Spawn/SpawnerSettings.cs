using Unity.Mathematics;
using Unity.Entities;

/// <summary>
/// Spawner runtime state. Tracks the progression of waves and spawning logic.
/// </summary>
public struct SpawnerState : IComponentData
{
    // Wave tracking
    public int CurrentWaveIndex;

    /// <summary> 
    /// Countdown timer for the currently active wave.
    /// </summary>
    public float WaveTimer;
    public bool IsWaveActive;

    /// <summary> 
    /// Tracks how many enemies have been spawned so far in the current wave.
    /// </summary>
    public int TotalEnemiesSpawnedInWave;

    /// <summary>
    /// Tracks the number of enemies killed in the current wave (used for Kill Percentage).
    /// </summary>
    public int EnemiesKilledInWave;

    /// <summary>
    /// Pre-calculated total of enemies to spawn in this wave (used to calculate the kill ratio). 
    /// </summary>
    public int TotalEnemiesToSpawnInWave;

    /// <summary> 
    /// The index of the spawn group currently being processed. 
    /// </summary>
    public int CurrentGroupIndex;

    /// 
    /// <summary> How many enemies are left to spawn in the current group. 
    /// </summary>
    public int RemainingSpawnsInGroup;

    // Global counter
    /// <summary> 
    /// Current number of active enemies in the entire game.
    /// </summary>
    public int ActiveEnemyCount;
}

/// <summary>
/// Defines the geometric behavior and origin of the spawning logic.
/// </summary>
public enum SpawnMode
{
    /// <summary>
    /// Random points in the entire planet.
    /// </summary>
    RandomInPlanet,

    /// <summary>
    /// Use a specific position.
    /// </summary>
    Zone,

    /// <summary>
    /// Opposite point from the player.
    /// </summary>
    PlayerOpposite,

    /// <summary>
    /// Around the player using min and max range.
    /// </summary>
    AroundPlayer
}

/// <summary>
/// Global spawning rules and limitations.
/// </summary>
public struct SpawnerSettings : IComponentData
{
    /// <summary> The absolute maximum number of enemies allowed in the game at once. </summary>
    public int MaxEnemies;
}

/// <summary>
/// Defines a single wave containing the rules to spawn enemies and advance progression.
/// </summary>
public struct Wave : IBufferElementData
{
    /// <summary> 
    /// Maximum time in seconds before automatically advancing to the next wave.
    /// </summary>
    public float Duration;

    /// <summary> 
    /// Required kill ratio (0.0 to 1.0) to advance to the next wave early.
    /// </summary>
    public float KillPercentage;

    /// <summary> 
    /// The starting index of this wave's groups within the SpawnGroup buffer.
    /// </summary>
    public int GroupStartIndex;

    /// <summary> 
    /// The number of spawn groups associated with this wave.
    /// </summary>
    public int GroupCount;

    /// <summary> 
    /// Pre-calculated during the Baking process to avoid looping over groups at runtime.
    /// </summary>
    public int TotalEnemyCount;
}

/// <summary>
/// Defines a specific group of enemies to spawn within a wave.
/// </summary>
public struct SpawnGroup : IBufferElementData
{
    public Entity Prefab;
    public int Amount;
    public SpawnMode Mode;

    /// <summary> 
    /// Specific world position (Used for Zone / Single spawn modes). 
    /// </summary>
    public float3 Position;

    /// <summary> 
    /// Minimum distance from the target (Used for AroundPlayer spawn mode). 
    /// </summary>
    public float MinRange;

    /// <summary> 
    /// Maximum distance from the target (Used for AroundPlayer spawn mode). 
    /// </summary>
    public float MaxRange;

    /// <summary>
    /// Delay in seconds between each individual enemy spawned in this group. 
    /// </summary>
    public float SpawnDelay;
}