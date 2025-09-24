using Unity.Entities;

[System.Serializable]
public struct ActiveSpell : IBufferElementData
{
    public ESpellID ID;
    public float BaseCooldown;
    public float CooldownTimer;
    int Level;
}

public struct CastSpellRequest : IComponentData
{
    public Entity Caster;
    public ESpellID SpellID;
    public float Damage;
    public float Area;
}