using Unity.Entities;

public struct CastSpellRequest : IComponentData
{
    /// <summary>
    /// Caster of the spell.
    /// </summary>
    public Entity Caster;
    public Entity Target;

    public int DatabaseIndex;
}
