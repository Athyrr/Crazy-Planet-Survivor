using Unity.Entities;

/// <summary>
/// Represents a buffer element that store damage to apply to an entity.
/// Provided by either a spell system or <see cref="CollisionSystem"/>
/// </summary>
public struct DamageBufferElement : IBufferElementData
{
    public int Damage;
    public ESpellTag Tag;
    public bool IsCritical;
}
