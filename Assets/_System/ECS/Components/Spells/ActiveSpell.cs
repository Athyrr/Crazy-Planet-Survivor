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

    // Tracking
    public float TotalDamageDealt;
}