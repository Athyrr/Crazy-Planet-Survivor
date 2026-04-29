using Unity.Entities;
using Unity.Mathematics;

public struct BurnEffect : IComponentData, IEnableableComponent
{
    public float DamageOnTick;
    public float RemainingTime;
    public float TickTimer;
    public float TickRate;
}

public struct SlowEffect : IComponentData, IEnableableComponent
{
    public float SpeedReductionMultiplier;
    public float DurationLeft;
}

public struct StunEffect : IComponentData, IEnableableComponent
{
    public float DurationLeft;
}

// todo add more effects like armor reduction, damage boost, heal over time, etc Add to ActiveEffectsAuthoring and ActiveEffectsSystem


/// <summary>
/// Stats that are currently applied to the entity, calculated from all active effects and buffs.
/// </summary>
public struct FinalStats : IComponentData
{
    public float MoveSpeed;
    public float GlobalDamageMultiplier;
    public float ArmorMultiplier;
    public float RangeMultiplier;
}

public struct ActiveKnockback : IComponentData
{
    public float3 Direction;
    public float InitialForce;
    public float DurationLeft;
    public float MaxDuration;
}