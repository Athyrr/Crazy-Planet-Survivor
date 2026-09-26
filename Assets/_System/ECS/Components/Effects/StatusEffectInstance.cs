using Unity.Entities;
using Unity.Mathematics;

/// <summary>Extensible effect kind (byte — room to grow past the 4 launch types).</summary>
public enum EffectType : byte { Burn = 0, Slow = 1, Stun = 2, Knockback = 3 }

/// <summary>Per-type stacking policy read from <see cref="EffectTypeConfigEntry"/>.
/// RefreshOnly = one instance per (Type, Source); on refresh, magnitude = max(old, new), duration resets.
/// StackCapped = multiple instances of the same (Type, Source) coexist up to MaxStacks (oldest evicted).</summary>
public enum EStackMode : byte { RefreshOnly = 0, StackCapped = 1 }

/// <summary>One active status effect on a target, from one emitting source. Multiple instances of the same
/// <see cref="Type"/> from *different* <see cref="Source"/> entities coexist — this is what lets a spell's
/// Burn and a lava zone's Burn tick independently on the same target instead of overwriting each other.
/// Not all fields are meaningful for every <see cref="Type"/> (<see cref="Direction"/> is Knockback-only) —
/// same accepted tradeoff already documented on <c>HitAction</c> for a Burst-safe union-like element.</summary>
[InternalBufferCapacity(8)]
public struct StatusEffectInstance : IBufferElementData
{
    public EffectType Type;
    /// <summary>The emitting entity (spell projectile, hazard zone, totem…) — not the caster. Query its
    /// components directly for any "what kind of source is this" rule (e.g. HasComponent&lt;HazardZone&gt;)
    /// rather than trusting a cached classification.</summary>
    public Entity Source;
    public float Magnitude;
    public float RemainingTime;
    /// <summary>Knockback only — world-space push direction captured at application time.</summary>
    public float3 Direction;
    /// <summary>Burn only — fractional damage carried across frames. A tick's per-frame damage
    /// (Magnitude × DeltaTime/TickRate) is sub-1 for every real spell's numbers; without this, the whole
    /// amount is discarded every single frame (C1, final whole-branch review). Accumulated every frame by
    /// <c>ActiveEffectsSystem.TickStatusEffectsJob</c>, flushed once it reaches at least one full
    /// <see cref="Magnitude"/> — not a smaller/more-frequent 1-damage dribble (that was a second bug found
    /// in a re-review of the first fix: it changed Burn's cadence from ~1 real tick every TickRate seconds
    /// at full Magnitude to many sub-ticks/second at 1 damage each, which silently let Burn dodge
    /// HealthSystem's flat per-hit armor mitigation almost entirely). The flush arithmetic itself lives in
    /// <c>StatusEffectFormulas.ComputeBurnTicks</c> (plain C#, unit-tested) — see BurnTickFlushTests.cs.
    /// Zero-valued and unused for every other <see cref="EffectType"/>.</summary>
    public float DamageAccumulator;
}

/// <summary>Append-only, parallel-safe request queued by hit-resolution jobs (ResolveHit.ApplyEffect) via
/// ECB.AppendToBuffer — the same deferred pattern already used for DamageBufferElement/HealBufferElement.
/// Drained once per frame by ActiveEffectsSystem's DrainApplyRequestsJob into the entity's real
/// StatusEffectInstance buffer, where the refresh-vs-add read-modify-write actually happens. This two-phase
/// split exists because two different AreaAttack zones can hit the same target in the same parallel
/// schedule — refresh-vs-add can't safely run on the hot parallel path.</summary>
public struct StatusEffectApplyRequest : IBufferElementData
{
    public EffectType Type;
    public Entity Source;
    public float Magnitude;
    public float Duration;
    public float3 Direction;
    public EStackMode StackMode;
    public int MaxStacks;
}

/// <summary>Authoring-time declaration: "this spell applies effect X". No magnitude/duration/tickrate here —
/// those are global per-type config (EffectTypeConfigSO), not per-spell. See spec §4.2 / Q2.
/// <c>[System.Serializable]</c> so it can live in an inspector-editable array on a ScriptableObject
/// (<c>SpellDataSO.Effects</c>, Task 18; <c>SpellUpgradeSO.GrantedEffects</c>, Task 6b) — added during the
/// 2026-09-25 correction pass: both consumers assumed an Inspector-editable array, and without this
/// attribute Unity silently drops the field from the Inspector instead of erroring.</summary>
[System.Serializable]
public struct EffectSpec
{
    public EffectType Type;
}

/// <summary>Mirror of the strongest active Slow instance, updated every frame by the unified tick job.
/// Read by ActiveEffectsSystem.ComposeLiveStatsJob (LiveStats.MoveSpeed).</summary>
public struct SlowState : IComponentData, IEnableableComponent { public float CurrentMultiplier; }

/// <summary>Mirror of Stun presence + remaining time (forward-compat for a UI timer). Read by
/// FlowFieldMovementSystem, EntitiesMovementSystem, DashSystem (presence only today).</summary>
public struct StunState : IComponentData, IEnableableComponent { public float RemainingTime; }

/// <summary>Mirror of the strongest active Burn instance (forward-compat for a UI icon/timer). Read by
/// VfxPresentationSystem (presence only today).</summary>
public struct BurnState : IComponentData, IEnableableComponent
{
    public float CurrentDamagePerTick;
    public float RemainingTime;
}

/// <summary>Full kinematic push state — renamed, unchanged-shape replacement for the old
/// <c>ActiveKnockback</c>. KnockbackSystem needs the whole struct (not just a scalar) to drive the tween,
/// so unlike the other three mirrors this one round-trips through StatusEffectInstance.Direction.</summary>
public struct KnockbackState : IComponentData, IEnableableComponent
{
    public float3 Direction;
    public float InitialForce;
    public float RemainingTime;
    public float MaxDuration;
}
