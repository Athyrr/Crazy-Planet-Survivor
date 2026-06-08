using System;

/// <summary>
/// Marks a <see cref="CoreStats"/> field as displayable in the auto-generated stat panels
/// (stat tab, character shop). Carries only what the central <see cref="StatsFormatUtils"/>
/// funnel cannot know on its own: the label, the linked stat (which drives the percent/flat +
/// color rules), the neutral baseline, and a couple of special-display flags.
///
/// The percent-vs-flat decision is NOT stored here anymore — it is derived centrally from
/// <see cref="Stat"/> via <see cref="StatsFormatUtils.IsPercentStat(ECharacterStat)"/>, so there
/// is a single source of truth shared with modifiers (amulets, upgrades, meta).
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public class UIStatAttribute : Attribute
{
    /// <summary>Human-readable label shown in the panel (e.g. "Move Sp.").</summary>
    public string DisplayName;

    /// <summary>Linked stat — drives the percent/flat + color rules through StatsFormatUtils.</summary>
    public ECharacterStat Stat;

    /// <summary>Baseline subtracted before display so the panel shows the delta (bonus) from neutral.</summary>
    public float NeutralValue;

    /// <summary>
    /// When true the raw value is shown as an absolute number with no leading sign
    /// (e.g. Max Health "100", Regen "1.5/s") instead of a signed delta.
    /// </summary>
    public bool Absolute;

    /// <summary>Optional suffix appended after the value (e.g. "/s").</summary>
    public string Suffix;

    /// <summary>Decimal places used for <see cref="Absolute"/> display (0 -> "100", 1 -> "1.5").</summary>
    public int Decimals;

    public UIStatAttribute(string displayName, ECharacterStat stat, float neutralValue = 0f,
        bool absolute = false, string suffix = "", int decimals = 0)
    {
        DisplayName = displayName;
        Stat = stat;
        NeutralValue = neutralValue;
        Absolute = absolute;
        Suffix = suffix;
        Decimals = decimals;
    }
}
