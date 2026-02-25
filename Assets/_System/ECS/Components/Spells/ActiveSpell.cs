using Unity.Entities;
/// <summary>
/// Represents a spell in runtime with its coolodwn and datas.
/// </summary>
public struct ActiveSpell : IBufferElementData
{
    public int DatabaseIndex;
    public int Level;

    public float CurrentCooldown;

    public ESpellTag AddedTags;

    public float DamageMultiplier;  // ex: 1.5 (+50%)
    public float CooldownMultiplier;// ex: 0.9 (-10%)
    public float AreaMultiplier;
    public float SpeedMultiplier;
    public float DurationMultiplier;
    public float RangeMultiplier;
    public float TickRateMultiplier;
    public float LifetimeMultiplier;

    public float CritChanceAdded;               //TODO check if can be deleted
    public float CritMultiplierMultiplier;      //TODO check if can be deleted

    public int BonusAmount;
    public int BonusBounces;
    public int BonusPierces;

    public float BonusCritChance;
    public float BonusCritMultiplier;
    public float SizeMultiplier;
}