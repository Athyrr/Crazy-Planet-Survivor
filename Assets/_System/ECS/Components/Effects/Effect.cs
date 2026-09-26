using Unity.Entities;

/// <summary>
/// Runtime stats consumed <b>tick-per-frame</b> by non-spell systems (movement, HealthSystem armor,
/// HealthRegenSystem, KnockbackSystem). Composed each frame by <c>ActiveEffectsSystem</c> from
/// <c>CoreStats</c> + sum of <see cref="CharacterStatBuff"/> entries + dedicated effects (Slow…).
/// Consumers read this component and NEVER <c>CoreStats</c> directly for these fields, so buffs and
/// debuffs are visible everywhere (see §9.2 of <c>SPELL_TAXONOMY</c>).
/// <br/><br/>
/// The 12 spell stats (Damage, Amount, Crit…) are NOT here — they are recomposed on-demand by
/// <c>SpellStatsCalculationSystem</c> and cached into <c>ActiveSpell.FinalX</c> (recomputing them
/// every frame would be wasted work).
/// </summary>
public struct LiveStats : IComponentData
{
    public float MoveSpeed;
    public float PickupRange;
    public float Armor;
    public float HealthRegen;
    public float KBResist;
}