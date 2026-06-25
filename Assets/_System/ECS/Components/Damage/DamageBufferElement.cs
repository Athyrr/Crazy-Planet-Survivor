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

    /// <summary>
    /// Category of the damage source, stamped by the producing system. Read by
    /// <see cref="HealthSystem"/> to drive the player's camera-shake intensity.
    /// </summary>
    public EDamageShakeSource ShakeSource;
}
