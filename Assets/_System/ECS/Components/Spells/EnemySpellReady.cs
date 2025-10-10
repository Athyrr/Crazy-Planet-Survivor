using Unity.Entities;

public struct EnemySpellReady : IBufferElementData
{
    public Entity Caster;
    public ActiveSpell Spell;
}
