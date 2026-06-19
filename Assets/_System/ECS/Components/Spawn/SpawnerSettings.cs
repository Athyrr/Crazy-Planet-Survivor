using Unity.Mathematics;
using Unity.Entities;

/// <summary>
/// Spawner runtime state. Per-wave progression (timer, kills) lives in the <see cref="WaveRuntime"/>
/// buffer so that several waves can be active at once (a "lead" wave plus looping waves repeating in
/// parallel behind it). Only the lead pointer and the global enemy count are scalars here.
/// </summary>
public struct SpawnerState : IComponentData
{
    /// <summary>
    /// Index of the "lead" wave — the frontier of sequential progression (preserves 1st, 2nd, 3rd...
    /// continuity). -1 means the spawner still has to initialize and start wave 0.
    /// </summary>
    public int CurrentWaveIndex;

    /// <summary>
    /// Current number of active enemies in the entire game (the hard cap is shared by every active wave).
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

    /// <summary>
    /// If true, once this wave finishes its first run (timeout or kill %) it keeps repeating its groups
    /// over and over, in parallel with the waves that follow. The first iteration still happens in
    /// sequence; subsequent iterations run as a background loop. Stripped at bake time for any wave that
    /// contains a final boss (a looping win-boss would respawn and end the run repeatedly).
    /// </summary>
    public bool Loop;

    /// <summary>
    /// If true, this whole wave's enemies spawn around the final boss's live position (a ring using each
    /// group's Min/MaxRange) instead of each group's normal mode. The wave does nothing until a final boss
    /// exists. Pace it with the wave's Duration/KillPercentage and each group's SpawnDelay.
    /// </summary>
    public bool AroundBoss;
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

    /// <summary>
    /// Uniform scale applied to spawned entities. 0 means "use the default" (1).
    /// Used to spawn larger entities such as bosses without shrinking them to 1.
    /// </summary>
    public float Scale;
}

/// <summary>
/// Per-group runtime spawn state, index-aligned 1:1 with the <see cref="SpawnGroup"/> buffer.
/// Lets every group of the active wave "popcorn" its enemies independently and in parallel,
/// each on its own <see cref="SpawnGroup.SpawnDelay"/> cadence.
/// </summary>
public struct SpawnGroupRuntime : IBufferElementData
{
    /// <summary> Enemies still to spawn for this group in the active wave. 0 = done / inactive. </summary>
    public int Remaining;

    /// <summary>
    /// Countdown until the next enemy pops. Reloaded with <see cref="SpawnGroup.SpawnDelay"/> after each spawn.
    /// Stays negative to carry a backlog (catch-up) when a frame is long or the frame budget is saturated.
    /// </summary>
    public float SpawnTimer;
}

/// <summary>
/// Per-wave runtime state, index-aligned 1:1 with the <see cref="Wave"/> buffer. Several entries can be
/// <see cref="Active"/> simultaneously: the lead wave plus any number of looping waves repeating in
/// parallel behind it.
/// </summary>
public struct WaveRuntime : IBufferElementData
{
    /// <summary> True while this wave is currently spawning/looping. </summary>
    public bool Active;

    /// <summary>
    /// Countdown for the current iteration. Reloaded with the wave's period whenever the wave is (re)armed.
    /// </summary>
    public float Timer;

    /// <summary>
    /// Enemies killed in the current iteration of this wave (drives the kill-percentage condition).
    /// Reset every time the wave is (re)armed.
    /// </summary>
    public int KilledCount;
}