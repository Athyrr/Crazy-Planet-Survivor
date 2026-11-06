using System;
using UnityEngine;

public enum EEmitterTrigger : byte { OnActivation = 0, OnImpact = 1 }
public enum EEmitterAttachment : byte { Detached = 0, Attached = 1 }
public enum EEmitterLayout : byte { AtPoint = 0, Circle = 1, Spread = 2 }
public enum EEmitterCount : byte { Fixed = 0, Amount = 1, SplitCount = 2 }
public enum EEmitterFrame : byte { Planet = 0, Facing = 1 }

/// <summary>One emitter declared on a SpellSO (max 2). The child SpellSO gives the FORM (prefab, lifetime,
/// speed, effects); the root spell gives the POWER (damage, size, crit). Presence of an entry = permission
/// for its capability. See spec generalized-emitter.</summary>
[Serializable]
public class SpellEmitterEntry
{
    [Tooltip("None = always active. Explode = needs the Explode capability granted. Split = active while FinalSplitCount > 0.")]
    public ESpellCapability Capability = ESpellCapability.None;
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
    [Tooltip("Attached: orbit frame (Planet = tangent plane, Facing = caster forward as angle origin).")]
    public EEmitterFrame Frame = EEmitterFrame.Planet;
    [Tooltip("Explode only: proc chance per resolved hit (1 = always). Deterministic per hit.")]
    [Range(0f, 1f)] public float Chance = 1f;
    [Tooltip("Explode only: damage ratio of the root FinalDamage. 0 = EmitterConfig.ExplosionDamageRatio.")]
    [Min(0f)] public float DamageRatio = 0f;
}
