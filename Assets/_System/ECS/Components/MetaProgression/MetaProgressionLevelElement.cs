using Unity.Entities;

/// <summary>
/// Stores the purchased level and computed total bonus of a meta-progression stat upgrade.
/// Added as a DynamicBuffer on the GameState entity, persisted between runs.
/// The total bonus is pre-computed by the UI at purchase time, so the runtime apply
/// system doesn't need ScriptableObject access.
/// </summary>
public struct MetaProgressionLevelElement : IBufferElementData
{
    /// <summary>
    /// Which stat this upgrade targets.
    /// </summary>
    public ECharacterStat Stat;

    /// <summary>
    /// Current purchased level (0 = not upgraded, max 5).
    /// </summary>
    public int Level;

    /// <summary>
    /// Pre-computed cumulative bonus value for this level.
    /// Stored as float delta (0.1 = +10%). Applied directly to CoreStats.
    /// </summary>
    public float TotalBonus;
}
