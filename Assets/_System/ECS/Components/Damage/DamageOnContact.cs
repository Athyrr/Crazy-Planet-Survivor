using Unity.Entities;

public struct DamageOnContact : IComponentData
{
    public float Damage;
    public ESpellElement Element;
    public float AreaRadius;
}