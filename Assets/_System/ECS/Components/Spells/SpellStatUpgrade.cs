using Unity.Entities;

/// <summary>
/// Buffer element that represents a <b>permanent</b> upgrade applied to spells sharing the required
/// tags. Added by <see cref="ApplyUpgradeSystem"/> (level-up upgrades, amulet modifiers) and consumed
/// by <see cref="SpellStatsCalculationSystem"/> when it recomputes each spell's Final stats.
/// <br/><br/>
/// For <i>temporary</i> stat changes on the caster (buffs / debuffs), use <c>CharacterStatBuff</c>
/// instead (§9.2 of SPELL_TAXONOMY). Naming matrix: <c>*Upgrade</c> = permanent, <c>*Buff</c> = temporary.
/// </summary>
public struct SpellStatUpgrade : IBufferElementData
{
    public ESpellTag RequiredTags;
    public ESpellStat SpellStat;
    public float Value;
    public EModiferStrategy Strategy;
}
