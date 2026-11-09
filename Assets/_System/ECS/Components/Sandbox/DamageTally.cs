using Unity.Entities;

/// <summary>Cumulative damage taken, written by HealthSystem.ApplyDamageJob: after armor, before the HealthFloor clamp
/// and the int cast (overkill included, needed on an immortal dummy). Dummies (baked) and the player during `ttk`.
/// Always compiled. Rolling DPS is computed by the reader from timed samples of Total.</summary>
public struct DamageTally : IComponentData
{
    public float Total;
    /// <summary>Biggest single DamageBufferElement after armor.</summary>
    public float MaxHit;
    public int Hits;
    public double FirstHit;
    public double LastHit;
}
