using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

public struct SpellBlob
{
    // Identity
    public ESpellID ID;
    // public FixedString512Bytes DisplayName; // 
    public ESpellTag Tag;

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

    // Children based spells
    public float ChildrenSpawnRadius;
    public int ChildPrefabIndex;
    
    // Amount
    public int BaseAmount;

    // Multi-cast (how Amount is consumed at cast time — see ESpellMultiCast)
    public ESpellMultiCast MultiCast;
    public float SpreadAngleDegrees;
    public float MaxSpreadDegrees;

    // Status effects this spell declares at authoring time (base Tags bits are derived from this — see
    // SpellSOEditor). Read at cast-stats time by SpellStatsCalculationSystem (Task 19c) as the base of
    // effectiveEffects = Effects[] + ActiveSpell.AddedEffects (upgrade-granted, Task 6c) — see spec §4.3
    // correction. Not read directly by ResolveHit/CollisionSystem/AreaAttackSystem; they read the already-
    // composed DamageOnContact.EffectsToApply/AreaAttack.EffectsToApply instead (Task 19b/19d).
    public BlobArray<EffectSpec> Effects;
}