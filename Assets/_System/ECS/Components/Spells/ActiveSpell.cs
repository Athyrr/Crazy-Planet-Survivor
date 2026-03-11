using Unity.Entities;

[InternalBufferCapacity(8)] 
public struct ActiveSpell : IBufferElementData
{
    public int DatabaseIndex;
    public int Level;
    public float CurrentCooldown;

    // INPUTS : Local Bonus
    public float LocalDamageBonusMultiplier; 
    public float LocalAreaBonusMultiplier;
    public float LocalSizeBonusMultiplier;
    public float LocalSpeedBonusPercent;
    public float LocalDurationBonusPercent;
    public float LocalCooldownBonusPercent; 
    public float LocalRangeBonusMultiplier;
    public float LocalTickRateBonusMultiplier;

    public int LocalAmountBonus;
    public int LocalBounceBonus;
    public int LocalPierceBonus;
    
    public float LocalBounceRangeBonusMultiplier;

    public float LocalCritChanceBonusPercent;
    public float LocalCritDamageBonus;
    
    public ESpellTag AddedTags;

    // OUTPUTS : Final values (cache)
    public float FinalDamage;
    public float FinalArea;
    public float FinalSize;
    public float FinalSpeed;
    public float FinalDuration;
    public float FinalCooldown; 
    
    public float FinalRange;
    public float FinalTickRate;
    
    public int FinalAmount;
    public int FinalBounces;
    public int FinalPierces;

    public float FinalBounceRange;
    
    public float FinalCritChance;
    public float FinalCritDamageMultiplier;

    // Tracking
    public float TotalDamageDealt;
}