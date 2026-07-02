using Unity.Entities;

/// <summary>
/// Per-player life-steal runtime state: the proc rate-limiter cooldown and the fractional-HP carryover
/// (Health is an int, but a proc heals Conversion × damage which is usually fractional).
/// </summary>
public struct LifeStealState : IComponentData
{
    /// <summary>Seconds until the next proc is allowed to heal (rate limiter, e.g. 0.1 = max 10/s).</summary>
    public float CooldownTimer;

    /// <summary>Fractional HP banked across procs until it forms a whole HP point.</summary>
    public float HealCarryover;
}

/// <summary>
/// One life-steal proc queued on the player this frame: <c>Heal = Conversion × damage dealt</c>. Producers
/// (collision/area/tick) append on a successful roll; <see cref="LifeStealSystem"/> drains the buffer and
/// applies at most one (the strongest) per cooldown window — the surplus is the rate-limited diminishing return.
/// </summary>
[InternalBufferCapacity(8)]
public struct LifeStealProcBufferElement : IBufferElementData
{
    public float Heal;
}
