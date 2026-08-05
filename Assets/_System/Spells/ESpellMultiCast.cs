/// <summary>
/// How <see cref="SpellDataSO.BaseAmount"/> (+ upgrades) is consumed at cast time.
/// Each spell picks the mode that matches its intent — this replaces the previous "always spread on
/// projectiles, stack on the same point for zones" behavior (E3 exploit: N auras superposées).
/// </summary>
public enum ESpellMultiCast : byte
{
    /// <summary>One instance per cast. Amount is ignored at cast time.
    /// Use for singular hits: novas, auras, slashes. (For Orbital, Amount still flows to the spawner's
    /// child count — a different consumer, unrelated to this mode.)</summary>
    Single = 0,

    /// <summary>N instances in a fan around the aim direction.
    /// <c>SpreadAngleDegrees</c> = angle between two adjacent instances (per-spell), capped by
    /// <c>MaxSpreadDegrees</c> total — a hard total-spread limit tightens the per-instance angle
    /// when N grows. Use for volley projectiles: Fireball, PoisonNeedle, ShockShard, ShockChain…</summary>
    Spread = 1,

    /// <summary>One instance per distinct enemy, up to N, taking the N nearest within <c>FinalRange</c>.
    /// Surplus (Amount &gt; distinct targets found) falls back to <see cref="Spread"/> from the caster.
    /// Zero targets found → the cast is cancelled cleanly. Player-cast only; enemy casts fall back to
    /// Spread (only one player target exists). Use for volley strikes: ShockStrike, lightnings.</summary>
    MultiTarget = 2,
}
