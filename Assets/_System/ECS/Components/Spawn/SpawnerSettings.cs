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
    AroundPlayer,

    /// <summary>
    /// Uniform ring around the player: every enemy sits on a single circle of radius MaxRange, at an
    /// angle of i*(2*PI/N) so the N enemies are equally spaced. MinRange is ignored. Set the group's
    /// SpawnDelay to 0 for an instant full ring; a positive delay makes them appear one-by-one sweeping
    /// around the circle.
    /// </summary>
    CircleAroundPlayer,

    /// <summary>
    /// Directional cluster: enemies fill an angular SECTOR (a "wall") centered on a bearing around the
    /// player, at a distance in [MinRange, MaxRange]. The bearing (SpawnCommand.ArcCenter) and the sector
    /// width (SpawnCommand.ArcWidth) are set by the caller — used by the assault director to throw walls of
    /// enemies from one or more chosen angles, rather than a full 360° ring.
    /// </summary>
    SectorAroundPlayer
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
/// Tunables for the "assault director": rhythmic pressure pulses layered ON TOP of the authored waves,
/// meant to create felt phases (calm baseline -> peak -> release) and the "cornered/swarm" feeling.
///
/// Each pulse fires two coordinated bursts, reusing the spawner's existing spawn job/modes:
///   - a NEAR ring around the player (<see cref="SpawnMode.CircleAroundPlayer"/>, <see cref="RingRadius"/>):
///     an inescapable encirclement — the "cornered" moment (a fast player can't outrun a ring spawned ON them).
///   - a FAR mass around the player (<see cref="SpawnMode.AroundPlayer"/>, <see cref="HorizonMinRange"/>..
///     <see cref="HorizonMaxRange"/>): a horde that walks in over the horizon — the "invasion" mass.
///
/// Both the pulse SIZE and FREQUENCY ramp up over run time (peaks get bigger and closer together, so the
/// run climaxes toward continuous pressure), keyed to <see cref="RunProgression.Timer"/>. The authored waves
/// remain the low ambient BETWEEN pulses — keep them light for the phases to read.
/// </summary>
public struct SpawnIntensityConfig : IComponentData
{
    /// <summary> Master switch. When false the director does nothing and only the authored waves spawn. </summary>
    public bool Enabled;

    /// <summary> Enemy prefab used for pulse bursts. Entity.Null = reuse the first authored group's prefab. </summary>
    public Entity AssaultPrefab;

    /// <summary> Grace period at run start before the first pulse (lets the player settle in). </summary>
    public float FirstPulseDelay;

    /// <summary> Seconds between pulses at run start (the slow, breathing early rhythm). </summary>
    public float PulsePeriodStart;

    /// <summary> Seconds between pulses at full ramp (fast late rhythm, near-continuous pressure). </summary>
    public float PulsePeriodMin;

    /// <summary> Ring enemy count per pulse at run start. </summary>
    public int RingCountStart;

    /// <summary> Ring enemy count per pulse at full ramp. </summary>
    public int RingCountMax;

    /// <summary> Radius (world units) of the near encirclement ring. Keep it tight for the "cornered" feel. </summary>
    public float RingRadius;

    /// <summary> Far-mass enemy count per pulse at run start (0 = no horizon mass). </summary>
    public int HorizonCountStart;

    /// <summary> Far-mass enemy count per pulse at full ramp. </summary>
    public int HorizonCountMax;

    /// <summary> Inner distance of the far mass — set beyond the visible horizon so they walk IN. </summary>
    public float HorizonMinRange;

    /// <summary> Outer distance of the far mass. </summary>
    public float HorizonMaxRange;

    /// <summary> Directional-wall enemy count per pulse at run start (0 = no directional walls). </summary>
    public int DirCountStart;

    /// <summary> Directional-wall enemy count per pulse at full ramp. </summary>
    public int DirCountMax;

    /// <summary> Inner distance of the directional walls — set beyond the horizon so they stream in. </summary>
    public float DirMinRange;

    /// <summary> Outer distance of the directional walls. </summary>
    public float DirMaxRange;

    /// <summary> Angular width (radians) of each directional wall. ~0.7 ≈ 40°. TAU (~6.28) = full circle. </summary>
    public float DirArcWidth;

    /// <summary> Max simultaneous walls per pulse (1..N): 2 = two masses converge from two angles at once. </summary>
    public int DirMaxClusters;

    /// <summary> Run seconds over which SIZE and FREQUENCY interpolate from "Start" to "Max/Min". </summary>
    public float RampSeconds;

    public static SpawnIntensityConfig Default => new SpawnIntensityConfig
    {
        Enabled = false,           // opt-in: unchanged behavior until an authoring turns it on
        AssaultPrefab = Entity.Null,
        FirstPulseDelay = 15f,
        PulsePeriodStart = 22f,
        PulsePeriodMin = 9f,
        RingCountStart = 35,
        RingCountMax = 100,
        RingRadius = 62f,
        HorizonCountStart = 12,
        HorizonCountMax = 40,
        HorizonMinRange = 45f,
        HorizonMaxRange = 75f,
        DirCountStart = 25,
        DirCountMax = 70,
        DirMinRange = 55f,
        DirMaxRange = 85f,
        DirArcWidth = 0.7f,
        DirMaxClusters = 2,
        RampSeconds = 300f
    };

    /// <summary> 0..1 ramp progress from the run timer (how far we are toward peak intensity). </summary>
    public float RampT(float timer)
    {
        if (RampSeconds <= 0f)
            return 1f;
        float t = timer / RampSeconds;
        return t < 0f ? 0f : (t > 1f ? 1f : t);
    }
}

/// <summary>
/// Runtime state for the <see cref="SpawnIntensityConfig"/> director. Lives on the spawner singleton entity.
/// A pulse is "armed" by seeding <see cref="RingRemaining"/>/<see cref="HorizonRemaining"/>, which then drain
/// through the spawner's shared per-frame budget (like a group's Remaining) until the burst is fully spawned.
/// </summary>
public struct SpawnIntensityState : IComponentData
{
    /// <summary> Countdown to the next pulse. Reloaded with the (ramped) pulse period when a pulse arms. </summary>
    public float PulseTimer;

    /// <summary> Ring enemies still to spawn for the current pulse (drained by the frame budget). </summary>
    public int RingRemaining;

    /// <summary> Ring total of the current pulse, for the CircleAroundPlayer angular layout. </summary>
    public int RingTotal;

    /// <summary> Far-mass enemies still to spawn for the current pulse. </summary>
    public int HorizonRemaining;

    /// <summary> Far-mass total of the current pulse, for the AroundPlayer layout. </summary>
    public int HorizonTotal;

    /// <summary> Directional-wall enemies still to spawn for the current pulse. </summary>
    public int DirRemaining;

    /// <summary> Directional-wall total of the current pulse. </summary>
    public int DirTotal;

    /// <summary> Number of simultaneous walls in the current pulse (1 or 2); enemies split across them. </summary>
    public int DirClusterCount;

    /// <summary> Bearing (radians, in the player's tangent frame) of directional cluster 0. </summary>
    public float DirCenter0;

    /// <summary> Bearing of directional cluster 1 (used when DirClusterCount == 2). </summary>
    public float DirCenter1;

    /// <summary> Prefab captured when the current pulse armed (resolved once, reused for its whole burst). </summary>
    public Entity PulsePrefab;
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