using Unity.Entities;

public struct DamageOnTick : IComponentData
{
    public float DamagePerTick;
    public ESpellTag Element;

    public float TickRate;
    public float ElapsedTime;

    public Entity Caster;
    public float AreaRadius;
}
