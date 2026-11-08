using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

// One validated emitter entry of a SpellSO (generalized-emitter).
// ChildIndex = position of the child SpellSO in GameSpellDatabase.Spells, resolved at bake.
public struct EmitterBlob
{
    public ESpellCapability Capability;
    public EEmitterTrigger Trigger;
    public EEmitterAttachment Attachment;
    public EEmitterLayout Layout;
    public EEmitterCount Count;
    public int FixedCount;
    public int ChildIndex;
    public float Radius;
    public float SpreadAngleDegrees;
    public EEmitterFrame Frame;
    public float Chance;
    public float DamageRatio;
    public float LineStartDistance;
    public float LineSpacing;
    public EEmitterTiming Timing;
    public float SequenceInterval;
}

// Summon of a SpellSO of the Summon family (spec allegeance-summon §2). Meaningful only when SpellBlob.IsSummon.
public struct SummonBlob
{
    public ESummonMobility Mobility;
    public ESummonOffense Offense;
    public ESummonTargeting Targeting;
    public ESummonEndOfLife EndOfLife;
    public bool TakesAggro;

    // Base stats (SummonSO.BuildBaseStats) and inheritance ratios (SummonSO.BuildRatios).
    public CoreStats BaseStats;
    public SummonInheritRatios Ratios;

    // DB indices of the spells the agent casts (resolved like emitter children).
    public BlobArray<int> AttackSpellIndices;

    public float RingMinRadius;
    public float RingMaxRadius;
    public float LeashRadius;
    public float Acceleration;
    public float MaxTurnRateDeg;
    // Follow catch-up: speed / acceleration × ReturnSpeedMultiplier while returning; teleport beyond
    // TeleportDistanceFactor × LeashRadius (0 = never).
    public float ReturnSpeedMultiplier;
    public float TeleportDistanceFactor;
}

public struct SpellBlob
{
    // Identity
    public ESpellID ID;
    // public FixedString512Bytes DisplayName; // 
    public ESpellTag Tag;
    // Capabilities the spell may use. Bounce/Pierce = SpellSO permission AND prefab slot (ruling D3); Explode/Split/Emit = a valid emitter entry of that capability. Read at cast time, in FinalTags derivation, by the upgrade filter and by EmitterQueries.IsEmitterActive.
    public ESpellCapability AllowedCapabilities;

    // Base Stats
    public float BaseDamage;
    public float BaseCooldown;
    public float BaseSpeed;
    public float Lifetime;
    public float BaseCastRange;
    public bool SizeScalesRange;
    public float3 BaseSpawnOffset;
    public float BaseSize;
    
    // Targeting
    public ESpellTargetingMode TargetingMode;

    // Ricochet
    public int Bounces;
    public float BounceRange;

    // Pierce
    public int Pierces;

    // Tick Effects
    public float TickRate;

    // Amount
    public int BaseAmount;

    // Multi-cast (how Amount is consumed at cast time — see ESpellMultiCast)
    public ESpellMultiCast MultiCast;
    public float SpreadAngleDegrees;
    public float MaxSpreadDegrees;

    // Status effects this spell declares at authoring time (base Tags bits are derived from this — see
    // SpellSOEditor). Read at cast-stats time by SpellStatsCalculationSystem as the base of
    // effectiveEffects = Effects[] + ActiveSpell.AddedEffects (upgrade-granted). Not read directly by ResolveHit/CollisionSystem/AreaAttackSystem; they read the already-
    // composed DamageOnContact.EffectsToApply/AreaAttack.EffectsToApply instead.
    public BlobArray<EffectSpec> Effects;

    // Emitters (generalized-emitter): validated entries only (invalid ones are logged and skipped at bake).
    public BlobArray<EmitterBlob> Emitters;
    public ESpellCapability BaseGrantedCapabilities;
    public int BaseSplitCount;
    public int BaseSplitDepth;
    public float ReHitDelayOverride;

    // Summon delivery family: IsSummon ⇔ SpellSO.Summon set.
    public bool IsSummon;
    public SummonBlob Summon;
}