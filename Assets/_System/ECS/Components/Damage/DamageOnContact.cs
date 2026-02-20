using Unity.Entities;

public struct DamageOnContact : IComponentData
{
    public float Damage;
    public ESpellTag Element;
    public float AreaRadius;
    public float CritIntensity;
}