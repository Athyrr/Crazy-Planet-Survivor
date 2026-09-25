using Unity.Collections;
using Unity.Entities;

[InternalBufferCapacity(8)]
public struct ActiveSpell : IBufferElementData
{
    public int DatabaseIndex;
    public int Level;
    public float CurrentCooldown;

    // Local Bonus : 0.1 -> +10%
    public float LocalDamageBonusMultiplier;
    public float LocalSizeBonusMultiplier;
    public float LocalSpeedBonusMultiplier;
    public float LocalSpellDurationBonusMultiplier;
    public float LocalCooldownReducBonusMultiplier;
    public float LocalRangeBonusMultiplier;
    public float LocalTickRateBonusMultiplier;

    public int LocalAmountBonus;
    public int LocalBounceBonus;
    public int LocalPierceBonus;

    public float LocalBounceRangeBonusMultiplier;

    public float LocalExplosionDamageBonusMultiplier;
    public float LocalExplosionSizeBonusMultiplier;
    
    public float LocalCritChanceBonusPercent;
    public float LocalCritDamageBonus;

    public float LocalLifeStealChanceBonus;

    public ESpellTag AddedTags;

    // Upgrade-granted effect deltas — the effect-DATA parallel to AddedTags above (spec §4.3 correction).
    // Populated by ApplyUpgradeSystem.ApplySpellUpgrade, deduped by EffectType (a repeated upgrade pick never
    // grows this past one entry per type — max 4 possible today).
    public FixedList32Bytes<EffectSpec> AddedEffects;

    // Final values (cache)
    public float FinalDamage;
    public float FinalSize;
    public float FinalSpeed;
    public float FinalDuration;
    public float FinalCooldown;

    public float FinalRange;
    public float FinalTickRate;

    public int FinalAmount;
    public int FinalPierces;
    public int FinalBounces;

    public float FinalBounceRange;

    public float FinalExplosionDamageBonusMultiplier;
    public float FinalExplosionSizeBonusMultiplier;

    public float FinalCritChance;
    public float FinalCritDamageMultiplier;

    // Total life-steal proc chance (global CoreStats.LifeStealChance + LocalLifeStealChanceBonus), clamped 0..1.
    public float FinalLifeStealChance;

    // Per-spell status-effect magnitude bonus (additive, composes with CoreStats.Global*Multiplier in ResolveHit.ApplyEffect).
    public float FinalBurnMagnitudeBonus;
    public float FinalSlowMagnitudeBonus;

    // Composed once per SpellStatsCalculationRequest by SpellStatsCalculationSystem (Task 19c): base
    // SpellBlob.Effects[] + this spell's AddedEffects, deduped by EffectType. SpellCastingSystem (Task 19d)
    // copies this onto the spawned entity's DamageOnContact/AreaAttack.EffectsToApply — CollisionSystem/
    // AreaAttackSystem never re-derive it from Tags at hit time.
    public FixedList32Bytes<EffectSpec> FinalEffects;

    // currentTags from SpellStatsCalculationSystem's per-spell loop, cached: (base Tag | AddedTags) with the
    // 4 status bits (Burn/Slow/Stun/Knockback) MASKED OUT and replaced by bits derived from FinalEffects —
    // never read Tags to decide whether to apply a status effect, only for cheap presence checks (RequiredTags
    // gating, UI, resistance calc). SpellCastingSystem (Task 19d) uses this instead of recomputing tags itself.
    public ESpellTag FinalTags;

    // Tracking
    public float TotalDamageDealt;
}