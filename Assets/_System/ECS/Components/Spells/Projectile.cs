using Unity.Entities;

public struct Projectile : IComponentData
{
    public float Damage;
    public ESpellElement Element;
}