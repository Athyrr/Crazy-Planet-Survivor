using Unity.Entities;

/// <summary>
/// Request to apply meta-progression bonuses to the player's CoreStats.
/// Added by PlayerSpawnerSystem, processed by ApplyUpgradeSystem (ApplyMetaProgressionJob).
/// Pattern matches ApplyAmuletRequest.
/// </summary>
public struct ApplyMetaProgressionRequest : IComponentData
{
    // Survival
    public float MaxHealthBonus;
    public float ArmorBonus;
    
    // Movement
    public float MoveSpeedBonus;
    public float PickupRangeBonus;

    // Offensive
    public float DamageBonus;
    public float AttackSpeedBonus;
    public float SpellSizeBonus;
    public float SpellSpeedBonus;
    public float SpellDurationBonus;
    public float CastRangeBonus;

    // Flat bonuses
    public int AmountBonus;
    public int PierceBonus;
    public int BounceBonus;

    // Critical
    public float CritChanceBonus;
    public float CritDamageBonus;

    // Misc
    public float LuckBonus;
    public float HealthRegenBonus;
}
