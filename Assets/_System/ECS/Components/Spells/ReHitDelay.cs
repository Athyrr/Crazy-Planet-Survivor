using Unity.Entities;

/// <summary>Per-instance re-hit delay (seconds) from SpellSO.ReHitDelayOverride; absent = EmitterConfig.DefaultReHitDelay.</summary>
public struct ReHitDelay : IComponentData
{
    public float Value;
}
