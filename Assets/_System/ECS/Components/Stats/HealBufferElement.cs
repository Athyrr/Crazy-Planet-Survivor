using Unity.Entities;

/// <summary>
/// A whole-HP heal to apply to an entity this frame. Symmetric counterpart of
/// <see cref="DamageBufferElement"/>: heal *producers* (life steal, health regen, pickups,
/// heal zones…) each compute their own magnitude and fractional carryover, then append a
/// whole-HP amount here and emit their own green feedback. The buffer is drained by
/// <see cref="HealthSystem"/>'s heal pass — the single authority that sums the amounts,
/// clamps to <c>CoreStats.MaxHealth</c>, orders heal after damage/death (no resurrection)
/// and performs the one write to <see cref="Health"/>.
/// </summary>
public struct HealBufferElement : IBufferElementData
{
    /// <summary>Whole HP to add (producers apply their own float→int carryover before writing).</summary>
    public int Amount;
}
