using System;
using UnityEngine;

public enum EEmitterTrigger : byte { OnActivation = 0, OnImpact = 1, OnArm = 2 }
public enum EEmitterAttachment : byte { Detached = 0, Attached = 1 }
public enum EEmitterLayout : byte { AtPoint = 0, Circle = 1, Spread = 2, Line = 3 }
public enum EEmitterCount : byte { Fixed = 0, Amount = 1, SplitCount = 2 }
public enum EEmitterFrame : byte { Planet = 0, Facing = 1 }
public enum EEmitterTiming : byte { Instant = 0, Sequential = 1 }

/// <summary>One emitter declared on a SpellSO (max 2). The child SpellSO gives the FORM (prefab, lifetime,
/// speed, own effects); the root spell gives the POWER (damage, size, crit) and, for None / Emit / Split, its
/// FinalEffects too. Presence of an entry = permission for its capability. See specs generalized-emitter (lot 1, lot 2).</summary>
[Serializable]
public class SpellEmitterEntry
{
    [Tooltip("None = always active. Explode = needs the Explode capability granted. Split = active while FinalSplitCount > 0. Emit = needs the Emit capability granted.")]
    public ESpellCapability Capability = ESpellCapability.None;
    [Tooltip("OnActivation = at cast. OnImpact = per resolved hit (projectiles). OnArm = once per instance, when its AreaAttack Burst arms (end of ActivationDelay), from the hitbox Offset point.")]
    public EEmitterTrigger Trigger = EEmitterTrigger.OnImpact;
    public EEmitterAttachment Attachment = EEmitterAttachment.Detached;
    public EEmitterLayout Layout = EEmitterLayout.AtPoint;
    public EEmitterCount Count = EEmitterCount.Fixed;
    [Tooltip("Count = Fixed only.")] [Min(1)] public int FixedCount = 1;
    [Tooltip("Child spell (FORM). Self-reference is allowed for Split only.")]
    public SpellSO Child;
    [Tooltip("Circle: radius in world units, scaled by the root's range multiplier.")]
    public float Radius = 8f;
    [Tooltip("Spread: degrees between two adjacent children.")]
    public float SpreadAngleDegrees = 30f;
    [Tooltip("Line: distance of the first child from the request point, metres × root FinalSize (arc on the planet).")]
    [Min(0f)] public float LineStartDistance = 0f;
    [Tooltip("Line: distance between two adjacent children, metres × root FinalSize (arc on the planet). Must be > 0.")]
    public float LineSpacing = 3f;
    [Tooltip("Attached: orbit frame (Planet = tangent plane, Facing = caster forward as angle origin).")]
    public EEmitterFrame Frame = EEmitterFrame.Planet;
    [Tooltip("Instant = every child this frame. Sequential = child i spawns i × SequenceInterval after child 0 (Detached, None / Emit only).")]
    public EEmitterTiming Timing = EEmitterTiming.Instant;
    [Tooltip("Sequential only: seconds between two children. Must be > 0.")]
    public float SequenceInterval = 0.15f;
    [Tooltip("Explode only: proc chance per resolved hit (1 = always). Deterministic per hit.")]
    [Range(0f, 1f)] public float Chance = 1f;
    [Tooltip("Explode and Emit: damage ratio of the root FinalDamage. 0 = default (EmitterConfig.ExplosionDamageRatio for Explode, 1.0 for Emit).")]
    [Min(0f)] public float DamageRatio = 0f;
}
