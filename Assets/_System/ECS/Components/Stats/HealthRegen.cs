using Unity.Entities;

/// <summary>
/// Per-entity health regeneration state consumed by <see cref="HealthRegenSystem"/>.
/// Every <see cref="TickInterval"/> seconds the entity heals
/// <c>CoreStats.HealthRecovery * elapsed</c>, carrying fractional HP across ticks
/// (since <see cref="Health.Value"/> is an integer).
/// Add this component to any entity that should regenerate (player, regen enemies, ...).
/// </summary>
public struct HealthRegen : IComponentData
{
    /// <summary>Seconds between regeneration ticks. Falls back to 1s when &lt;= 0.</summary>
    public float TickInterval;

    /// <summary>Time accumulated since the last applied tick.</summary>
    public float Timer;

    /// <summary>Fractional HP carried over between ticks (Health is integer).</summary>
    public float Carryover;
}
