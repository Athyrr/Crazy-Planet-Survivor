using Unity.Entities;

/// <summary>Health never drops below max(1, Floor): HealthSystem clamps there, so the death block is never reached
/// (no DestroyEntityFlag, ExplodeOnDeath, EnemyKilledEvent, EndRunRequest). Dummy (baked, RegenDelay 3),
/// enemies_immortal (posed by SandboxModeApplySystem) and the Sandbox player (HealOnFloor, RegenDelay -1).
/// Healing is done by HealthFloorRegenSystem through HealBufferElement. Always compiled.</summary>
public struct HealthFloor : IComponentData
{
    public int Floor;
    /// <summary>Full heal after this many seconds without a damaging hit; &lt; 0 = never on a timer.</summary>
    public float RegenDelay;
    /// <summary>Full heal right after the floor caught a lethal blow (the player's "respawn in place").</summary>
    public bool HealOnFloor;
    /// <summary>Set by HealthSystem when a lethal blow was caught, consumed by HealthFloorRegenSystem.</summary>
    public bool PendingFloorHeal;
    /// <summary>World time (SystemAPI.Time.ElapsedTime) of the last damaging hit.</summary>
    public double LastHitTime;
    /// <summary>Lethal blows caught since the last drain (player deaths counter).</summary>
    public int FloorHits;
}
