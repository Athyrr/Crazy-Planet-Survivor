using Unity.Entities;
using UnityEngine;

[System.Serializable]
public struct ActiveSpell : IBufferElementData
{
    public ESpellID ID;
    public Entity SpellPrefab;
    public float BaseCooldown;
    public float CooldownTimer;
    public float Damage;
    public float Area;
    public float Range;
    public ESpellElement Element;
    int Level;
}

public struct CastSpellRequest : IComponentData
{
    public Entity Caster;
    public ESpellID SpellID;
}

public struct EnemySpellReady : IBufferElementData
{
    public Entity Caster;   
    public ActiveSpell Spell;
}