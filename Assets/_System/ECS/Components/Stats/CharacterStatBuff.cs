using Unity.Entities;

/// <summary>
/// A single <b>temporary</b> stat modifier on a character entity (player or enemy). Additive:
/// <c>Value &gt; 0</c> is a buff, <c>Value &lt; 0</c> is a debuff (game-design convention — the "buff bar"
/// covers both). Producers (auras, timed pickups, upgrades on a timer…) append entries to this buffer;
/// <c>CharacterStatBuffSystem</c> ticks <see cref="Remaining"/> and drops expired entries; the sum is
/// composed into <c>LiveStats</c> (tick stats) and read on-demand by <c>SpellStatsCalculationSystem</c>
/// (spell stats) — see §9.2 of <c>SPELL_TAXONOMY</c>.
/// <br/><br/>
/// For <i>permanent</i> character upgrades (level-up, amulet), use <see cref="CharacterStatUpgradeSO"/>
/// which writes directly to <c>CoreStats</c>.
/// <br/><br/>
/// Kept out of this channel: behavioural effects (Burn/Stun/Knockback) and multi-source CC with
/// "strongest wins" stacking (Slow) — those stay dedicated components (<see cref="SlowEffect"/>, etc.).
/// <br/><br/>
/// <b>Producer contract:</b> when appending an entry, also add a
/// <c>SpellStatsCalculationRequest</c> tag on the same entity, so the (on-demand) spell calc picks up
/// the new delta. Expirations are handled by <c>CharacterStatBuffSystem</c>, which emits the request
/// itself when it removes any entry.
/// </summary>
public struct CharacterStatBuff : IBufferElementData
{
    /// <summary>Which character stat to modify (see <see cref="ECharacterStat"/>).</summary>
    public ECharacterStat Stat;

    /// <summary>Additive delta on the stat. Negative = debuff.</summary>
    public float Value;

    /// <summary>Time left in seconds. When it reaches 0 the entry is removed.</summary>
    public float Remaining;
}
