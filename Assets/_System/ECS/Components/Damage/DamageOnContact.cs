using Unity.Entities;

public struct DamageOnContact : IComponentData, IEnableableComponent
{
    public float Damage;
    public ESpellTag Tag;
    public float AreaRadius;
    // public bool IsCritical;
    public float TotalCritChance;
    public float TotalCritMultiplier;
}