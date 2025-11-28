using Unity.Entities;
/// <summary>
/// Represent a projectile amount that can be increased by an upgrade.
/// </summary>
public struct IncreasableProjectileAmountData : IComponentData
{
    public int CurrentAmount;
    public int TargetAmount;
}
