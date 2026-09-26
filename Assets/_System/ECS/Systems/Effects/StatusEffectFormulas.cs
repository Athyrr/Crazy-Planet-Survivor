public static class StatusEffectFormulas
{
    /// <summary>Burn: ratio × the triggering hit's pre-crit damage. The crit roll of the hit that applies
    /// the Burn never affects the Burn's own magnitude — matches current behavior, confirmed by design.</summary>
    public static float ComputeBurnMagnitude(float ratio, float preCritHitDamage) => ratio * preCritHitDamage;

    /// <summary>Slow/Stun/Knockback: flat, config-driven — no natural quantity on the hit to derive from.</summary>
    public static float ComputeFlatMagnitude(float baseMagnitude) => baseMagnitude;

    /// <summary>Pure arithmetic for flushing a Burn instance's fractional damage accumulator: given how much
    /// has accumulated and the instance's per-tick Magnitude, how many whole Magnitude-sized ticks fire this
    /// call, and what's left over afterward. No ECS/Burst dependencies — deliberately isolated here so it's
    /// directly unit-testable (see BurnTickFlushTests.cs) — this exact arithmetic has broken twice already,
    /// in two different ways (discarding everything under 1 damage; then flushing arbitrary 1-damage
    /// dribbles instead of whole ticks), so it no longer lives inlined in
    /// ActiveEffectsSystem.TickStatusEffectsJob where neither bug could be caught without a live game.
    /// A <paramref name="magnitude"/> &lt;= 0 never ticks (0 ticks, accumulator returned unchanged) — a Burn
    /// instance should never legitimately have a non-positive per-tick Magnitude, but this keeps the
    /// function total instead of dividing by zero. A <paramref name="magnitude"/> under 1 (e.g. 0.5) still
    /// ticks normally once the accumulator reaches it — there is no minimum-magnitude floor here; that was
    /// the bug (a stale `if (damage &lt; 1f) return` guard at the call site, since removed).</summary>
    public static int ComputeBurnTicks(float accumulator, float magnitude, out float remainder)
    {
        if (magnitude <= 0f || accumulator < magnitude)
        {
            remainder = accumulator;
            return 0;
        }

        int ticks = (int)(accumulator / magnitude);
        remainder = accumulator - ticks * magnitude;
        return ticks;
    }
}
