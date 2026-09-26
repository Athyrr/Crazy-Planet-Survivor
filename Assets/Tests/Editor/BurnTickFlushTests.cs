using NUnit.Framework;

/// <summary>Covers StatusEffectFormulas.ComputeBurnTicks — the pure "how many whole ticks fire from a
/// fractional accumulator" arithmetic behind Burn's damage flush. This exact mechanic has broken twice
/// already, in two different ways, neither of which was caught before reaching a human playtester:
///   1. C1 (final whole-branch review): every real spell's per-frame Burn damage is sub-1, and the original
///      code discarded it outright every frame — Burn dealt zero damage on every spell.
///   2. C1's own fix, on re-review: flushing "an integer once the accumulator crosses 1" changed Burn's
///      cadence from ~1 real tick every TickRate seconds at full Magnitude to many 1-damage sub-ticks/second
///      — HealthSystem's flat per-hit armor mitigation is paid once per *hit*, so the same total damage
///      spread across many more, smaller hits silently let Burn dodge armor almost entirely.
/// Isolating the arithmetic in a plain C# method (no ECS/Burst) is what makes these regressions testable
/// without a live game or a Unity compiler in the loop.</summary>
public class BurnTickFlushTests
{
    [Test]
    public void Fireball_OneMagnitudeWorth_FiresExactlyOneTick()
    {
        // Fireball: 65 base damage, Burn ratio 0.125 -> Magnitude ~8.125. One TickRate window's worth of
        // accumulation crosses exactly one Magnitude.
        const float magnitude = 8.125f;
        const float accumulator = 8.125f;

        int ticks = StatusEffectFormulas.ComputeBurnTicks(accumulator, magnitude, out float remainder);

        Assert.AreEqual(1, ticks, "Exactly one Magnitude's worth of accumulation must fire exactly one tick.");
        Assert.AreEqual(0f, remainder, 1e-4f, "No leftover once the accumulator lands exactly on one Magnitude.");
    }

    [Test]
    public void BelowMagnitude_FiresNoTicks_RemainderUnchanged()
    {
        const float magnitude = 8.125f;
        const float accumulator = 3.4f; // well below one Magnitude

        int ticks = StatusEffectFormulas.ComputeBurnTicks(accumulator, magnitude, out float remainder);

        Assert.AreEqual(0, ticks, "An accumulator below Magnitude must not fire a tick — this is exactly the case C1 originally discarded instead of carrying forward.");
        Assert.AreEqual(accumulator, remainder, 1e-4f, "The full accumulator must be carried forward unchanged when no tick fires.");
    }

    [Test]
    public void MultipleMagnitudesWorth_FiresMultipleTicks_InOneCall()
    {
        // A frame hitch (or a source that was overshadowed for a while and just became strongest) can bank
        // several whole Magnitudes' worth before its next flush — all of them must fire as discrete
        // Magnitude-sized ticks in this one call, not a single lump.
        const float magnitude = 8.125f;
        const float accumulator = magnitude * 3.5f; // 3 whole ticks + a half-tick remainder

        int ticks = StatusEffectFormulas.ComputeBurnTicks(accumulator, magnitude, out float remainder);

        Assert.AreEqual(3, ticks, "3.5x Magnitude banked must fire exactly 3 whole ticks.");
        Assert.AreEqual(magnitude * 0.5f, remainder, 1e-3f, "The 0.5x-Magnitude remainder must be carried forward, not dropped.");
    }

    [Test]
    public void MagnitudeUnderOne_StillFiresATick_ProvesTheStaleGuardIsGone()
    {
        // This is the exact scenario a stale `if (damage < 1f) return;` guard at the call site used to
        // mishandle: a legitimate Magnitude below 1 (e.g. a weak DoT). The old guard fired AFTER the
        // accumulator had already been decremented by the caller, so that tick's damage was thrown away
        // silently and permanently — reintroducing C1's original zero-damage bug for any Magnitude < 1.
        // ComputeBurnTicks itself has no such floor: a tick fires the moment the accumulator reaches the
        // (however small, still positive) Magnitude.
        const float magnitude = 0.5f;
        const float accumulator = 0.5f; // exactly one Magnitude's worth

        int ticks = StatusEffectFormulas.ComputeBurnTicks(accumulator, magnitude, out float remainder);

        Assert.AreEqual(1, ticks,
            "A Magnitude < 1 must still fire a tick once the accumulator reaches it — proves the stale " +
            "`damage < 1f` guard (since removed from ActiveEffectsSystem.TickBurnDamage) is not silently " +
            "re-discarding this tick's damage.");
        Assert.AreEqual(0f, remainder, 1e-4f);
    }

    [Test]
    public void NonPositiveMagnitude_NeverTicks_AccumulatorUnchanged()
    {
        // Defensive: a Burn instance should never legitimately have a non-positive Magnitude, but the
        // function must stay total (no divide-by-zero) rather than assume it can't happen.
        const float accumulator = 5f;

        int ticksZero = StatusEffectFormulas.ComputeBurnTicks(accumulator, 0f, out float remainderZero);
        int ticksNegative = StatusEffectFormulas.ComputeBurnTicks(accumulator, -2f, out float remainderNegative);

        Assert.AreEqual(0, ticksZero);
        Assert.AreEqual(accumulator, remainderZero, 1e-4f);
        Assert.AreEqual(0, ticksNegative);
        Assert.AreEqual(accumulator, remainderNegative, 1e-4f);
    }
}
