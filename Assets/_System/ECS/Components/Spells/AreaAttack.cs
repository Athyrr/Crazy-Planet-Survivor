using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Cadence of an <see cref="AreaAttack"/> — how often it damages a given target.
/// </summary>
public enum EZoneCadence : byte
{
    /// <summary>Hits each target once over an active window (VoidSlash, ShockStrike, FreezingBlow…).
    /// Dedup via <see cref="HitEntityMemory"/>; shape may animate (Expand/Sweep).</summary>
    Burst,

    /// <summary>Re-hits targets on a tick cadence (FrozenZone, PoisonFloor auras…).
    /// Enter/exit tracked via <see cref="TickDamageTarget"/>.</summary>
    OverTime,
}

/// <summary>
/// Unified area-of-effect delivery: a region that damages what it overlaps. Merges the former
/// AreaAttack (Burst one-shot) and DamageOnTick (OverTime aura) components — the <see cref="Cadence"/>
/// field discriminates the two behaviors, processed by <c>AreaAttackSystem</c>.
///
/// Burst    : evaluated over [ActivationDelay, ActivationDelay + ActiveDuration]; RadiusStart→RadiusEnd
///            and SweepStart→SweepEnd interpolate over the window; each target hit once.
/// OverTime : RadiusStart == RadiusEnd (static, refreshed by size upgrades); re-hits every TickRate.
/// </summary>
public struct AreaAttack : IComponentData
{
    public EZoneCadence Cadence;

    // ── Shape (shared) ──
    public EAttackAreaShape Shape;
    public float RadiusStart;       // OverTime: == RadiusEnd (static). Burst: animated (Expand).
    public float RadiusEnd;
    public float PrefabRadius;      // base radius from prefab, for size-scaling (OverTime refresh)

    public float HalfAngle;         // Cone: aperture half-angle in radians
    public float SweepStart;        // Cone: initial center rotation relative to forward (radians)
    public float SweepEnd;          // Cone: final center rotation relative to forward (radians)
    public float RingThickness;     // Ring: width of the ring band

    // Local offset of the hitbox from the entity origin (entity-space, rotation + scale aware), so the
    // collision lines up with a forward-offset visual — e.g. a slam landing in front of the caster.
    // Points toward the target because the entity faces the target. Leave zero for centered shapes/cones.
    public float3 Offset;

    // ── Burst timing ──
    public float ActivationDelay;   // seconds before first evaluation
    public float ActiveDuration;    // how long collision evaluation runs

    // ── OverTime timing ──
    public float TickRate;          // seconds between ticks

    // ── Runtime timer (shared) ──
    public float ElapsedTime;

    // ── Combat (shared) ──
    public float Damage;            // per-hit (Burst) or per-tick (OverTime)
    public float CritChance;
    public float CritMultiplier;
    public Entity Caster;
    public uint TargetLayers;
    public ESpellTag Tags;
}
