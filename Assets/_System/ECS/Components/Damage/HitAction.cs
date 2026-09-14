using Unity.Entities;

/// <summary>
/// Which outcome a single <see cref="HitAction"/> applies. <b>Atomic</b> — one kind per action.
/// A spell that both damages and debuffs is a <i>list</i> of actions (a future
/// <c>DynamicBuffer&lt;HitAction&gt;</c>), not one multi-outcome struct: <c>[Damage(...), ApplyBuff(Armor,-x)]</c>.
/// See §9.4 / §10 of <c>SPELL_TAXONOMY.md</c>.
/// </summary>
public enum EHitKind : byte
{
    /// <summary>Deal damage → <see cref="DamageBufferElement"/> (+ tag effects, life steal, tracking).</summary>
    Damage,

    /// <summary>Restore HP → <see cref="HealBufferElement"/> (§9.1).</summary>
    Heal,

    /// <summary>Apply a temporary buff/debuff (numeric stat modifier) → <see cref="CharacterStatBuff"/>
    /// (+ <c>SpellStatsCalculationRequest</c>) (§9.2). <c>Value &lt; 0</c> = debuff, under the "buff" umbrella.</summary>
    ApplyBuff,
}

/// <summary>
/// Who an action is allowed to land on. Trivial today — <see cref="Self"/> = caster,
/// <see cref="Enemies"/> = the current <c>TargetLayers</c> (hardcoded). It's the forward hook for the
/// team/allegiance system (§8): <b>target selection still happens at the call site</b> (the physics
/// filter), so <see cref="ResolveHit"/> trusts the target it's given and only carries this for future routing.
/// </summary>
public enum EAllegiance : byte { Enemies, Self, Allies, All }

/// <summary>How a stat modifier composes. <see cref="Add"/> = additive delta (default; anti-stack, §11);
/// <see cref="Mult"/> = multiplicative (reserved).</summary>
public enum EStatOp : byte { Add, Mult }

/// <summary>
/// One <b>atomic</b> result to apply to a target, consumed by <see cref="ResolveHit"/>. Tagged by
/// <see cref="Kind"/>; only that kind's fields are meaningful (the others are unused — the accepted cost
/// of a Burst value type that doubles as the element of a future on-hit effect list). Build with the
/// <c>Make*</c> helpers for readability at call sites.
/// </summary>
public struct HitAction
{
    public EHitKind Kind;

    /// <summary>Forward-compatible team hook (§8). Selection stays at the call site for now.</summary>
    public EAllegiance Target;

    // ── Kind == Damage ──
    /// <summary>Base damage, pre-crit.</summary>
    public float Damage;
    /// <summary>Crit probability in [0,1].</summary>
    public float CritChance;
    /// <summary>Multiplier applied on a crit (clamped to &gt;= 1 by <see cref="ResolveHit"/>).</summary>
    public float CritMultiplier;
    /// <summary>Drives tag effects (Burn/Slow/Stun/Knockback), life-steal eligibility and damage tracking.</summary>
    public ESpellTag Tags;

    // ── Kind == Heal ──
    /// <summary>Whole HP to restore (producers apply their own float→int carryover before building this).</summary>
    public float HealAmount;

    // ── Kind == ApplyBuff ──
    public ECharacterStat Stat;
    public EStatOp Op;
    public float StatValue;
    /// <summary>Buff/debuff lifetime in seconds.</summary>
    public float Duration;

    /// <summary>A damage result. <paramref name="critMultiplier"/> is applied only on a crit roll.</summary>
    public static HitAction MakeDamage(float damage, float critChance, float critMultiplier,
        ESpellTag tags, EAllegiance target = EAllegiance.Enemies) => new HitAction
    {
        Kind = EHitKind.Damage,
        Target = target,
        Damage = damage,
        CritChance = critChance,
        CritMultiplier = critMultiplier,
        Tags = tags,
    };

    /// <summary>An instant heal. Defaults to the caster (<see cref="EAllegiance.Self"/>).</summary>
    public static HitAction MakeHeal(float amount, EAllegiance target = EAllegiance.Self) => new HitAction
    {
        Kind = EHitKind.Heal,
        Target = target,
        HealAmount = amount,
    };

    /// <summary>A temporary buff/debuff (numeric stat modifier). Negative <paramref name="value"/> = debuff.</summary>
    public static HitAction MakeApplyBuff(ECharacterStat stat, EStatOp op, float value, float duration,
        EAllegiance target = EAllegiance.Enemies) => new HitAction
    {
        Kind = EHitKind.ApplyBuff,
        Target = target,
        Stat = stat,
        Op = op,
        StatValue = value,
        Duration = duration,
    };
}

/// <summary>
/// The "who / where" of a hit, precomputed by the call site so <see cref="ResolveHit"/> stays free of
/// boss/spell-source lookups. Only meaningful for <see cref="EHitKind.Damage"/>.
/// </summary>
public struct HitSource
{
    /// <summary>The caster entity; <see cref="Entity.Null"/> if none. Gates player life-steal.</summary>
    public Entity Caster;

    /// <summary>Spell DB index for damage tracking / life-steal lookup; <c>-1</c> = no source (skip both).</summary>
    public int DatabaseIndex;

    /// <summary>Knockback pushes the target away from this point (player position on contact, zone centre for auras).</summary>
    public Unity.Mathematics.float3 PushOrigin;

    /// <summary>Camera-shake category, resolved by the caller (contact vs area vs DoT differ).</summary>
    public EDamageShakeSource Shake;
}
