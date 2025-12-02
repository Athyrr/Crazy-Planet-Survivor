using Unity.Entities;

public struct DamageOnTick : IComponentData
{
    public float DamagePerTick;
    public ESpellElement Element;

    public float TickRate;

    public Entity Caster;
}
