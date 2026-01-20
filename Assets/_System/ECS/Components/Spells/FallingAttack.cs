using Unity.Entities;

public struct FallingAttack : IComponentData
{
    public float Damage;
    public ESpellTag Element;
}
