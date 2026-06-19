using System.Text.RegularExpressions;
using UnityEngine;
using _System.Settings;

/// <summary>
/// Centralized formatting for displaying stats and stat modifiers in the UI.
/// Single source of truth for the sign / percent-vs-flat / color conventions shared across the
/// upgrade cards, the amulet / character / meta shops, the stat tab and the game-over summary.
///
/// Conventions:
/// <list type="bullet">
/// <item>Positive (and zero) values are shown in <see cref="PositiveColor"/> (green), negatives in <see cref="NegativeColor"/> (red).</item>
/// <item>Percentage stats always keep their '%', including 0% (which stays green).</item>
/// <item>Health stats (MaxHealth / Health / HealthRegen) and count stats (Pierce / Bounce / Amount)
/// are always flat values (+X / -X), never percentages.</item>
/// </list>
/// </summary>
public static class StatsFormatUtils
{
    /// <summary>Rich-text color for positive (bonus) values — also used for zero. Sourced from <see cref="CpUISettings.StatBonusColor"/>.</summary>
    public static string PositiveColor => "#" + ColorUtility.ToHtmlStringRGB(CpUISettings.StatBonusColor);

    /// <summary>Rich-text color for negative (malus) values. Sourced from <see cref="CpUISettings.StatMalusColor"/>.</summary>
    public static string NegativeColor => "#" + ColorUtility.ToHtmlStringRGB(CpUISettings.StatMalusColor);

    /// <summary>Neutral (grey) rich-text color, used for the "before" side of a before → after preview.</summary>
    public static string NeutralColor => "#" + ColorUtility.ToHtmlStringRGB(CpUISettings.LabelColor);

    // ----------------------------------------------------------------------------------
    // Naming
    // ----------------------------------------------------------------------------------

    /// <summary>Inserts spaces before inner capitals: "MaxHealth" -> "Max Health".</summary>
    public static string Humanize(string camelCase)
    {
        if (string.IsNullOrEmpty(camelCase))
            return camelCase;

        return Regex.Replace(camelCase, "(\\B[A-Z])", " $1");
    }

    // ----------------------------------------------------------------------------------
    // Stat classification (single source of truth for percent vs flat)
    // ----------------------------------------------------------------------------------

    /// <summary>Health stats are always displayed as flat values (+X / -X), never percentages.</summary>
    public static bool IsHealthStat(ECharacterStat stat)
        => stat is ECharacterStat.MaxHealth or ECharacterStat.Health or ECharacterStat.HealthRegen;

    /// <summary>Integer "count" stats (added projectiles / pierces / bounces) are flat values.</summary>
    public static bool IsCountStat(ECharacterStat stat)
        => stat is ECharacterStat.PierceCount or ECharacterStat.BounceCount or ECharacterStat.Amount or ECharacterStat.Luck;

    /// <summary>True when the character stat is displayed as a percentage (everything but health + counts).</summary>
    public static bool IsPercentStat(ECharacterStat stat)
        => !IsHealthStat(stat) && !IsCountStat(stat);

    /// <summary>True when the spell stat is displayed as a percentage (everything but the count stats).</summary>
    public static bool IsPercentStat(ESpellStat stat)
        => stat is not (ESpellStat.Amount or ESpellStat.BounceCount or ESpellStat.PierceCount);

    // ----------------------------------------------------------------------------------
    // Low-level signed formatting
    // ----------------------------------------------------------------------------------

    /// <summary>Signed percentage from a ratio: 0.1 -> "+10%", -0.2 -> "-20%", 0 -> "0%".</summary>
    public static string SignedPercent(float ratio)
    {
        float pct = ratio * 100f;
        return (pct >= 0f ? "+" : "") + pct.ToString("0") + "%";
    }

    /// <summary>Signed flat value: 3 -> "+3", -2 -> "-2", 0 -> "0".</summary>
    public static string SignedFlat(float value)
    {
        return (value >= 0f ? "+" : "") + value.ToString("0.##");
    }

    /// <summary>Formats a value as either a signed percentage or a signed flat number.</summary>
    public static string SignedValue(float value, bool isPercentage)
    {
        return isPercentage ? SignedPercent(value) : SignedFlat(value);
    }

    // ----------------------------------------------------------------------------------
    // Color
    // ----------------------------------------------------------------------------------

    /// <summary>
    /// Wraps text in a rich-text color tag based on the numeric value: green for >= 0
    /// (including 0 / 0%), red for negatives. Prefer this when the numeric value is available.
    /// </summary>
    public static string Colorize(string text, float value)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        string color = value < 0f ? NegativeColor : PositiveColor;
        return $"<color={color}>{text}</color>";
    }

    /// <summary>
    /// Wraps text in a color tag based on its leading sign. A leading '-' is red, everything else
    /// (including an unsigned "0%") is green. Use when only the formatted string is available.
    /// </summary>
    public static string ColorizeBySign(string formatted)
    {
        if (string.IsNullOrEmpty(formatted))
            return formatted;

        string color = formatted.StartsWith("-") ? NegativeColor : PositiveColor;
        return $"<color={color}>{formatted}</color>";
    }

    /// <summary>Wraps text in the neutral grey color (used for the "before" value of a preview).</summary>
    public static string Neutralize(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        return $"<color={NeutralColor}>{text}</color>";
    }

    // ----------------------------------------------------------------------------------
    // Before → after previews (upgrade cards)
    // ----------------------------------------------------------------------------------

    /// <summary>
    /// "before → after" preview for a character stat: the current value in neutral grey, an arrow,
    /// then the resulting value colored by the sign of the change (green = improvement, red = malus).
    /// Absolute stats (health + Luck) are shown as plain totals; percent stats keep their signed '%';
    /// count bonuses (Amount / Pierce / Bounce) are signed flats.
    /// </summary>
    public static string FormatStatBeforeAfter(ECharacterStat stat, float before, float after)
    {
        bool absolute = IsHealthStat(stat) || stat == ECharacterStat.Luck;
        bool percent = !absolute && IsPercentStat(stat);

        string beforeStr, afterStr;
        if (percent)
        {
            beforeStr = SignedPercent(before);
            afterStr = SignedPercent(after);
        }
        else if (absolute)
        {
            beforeStr = before.ToString("0.##");
            afterStr = after.ToString("0.##");
        }
        else
        {
            beforeStr = SignedFlat(before);
            afterStr = SignedFlat(after);
        }

        return Neutralize(beforeStr) + " → " + Colorize(afterStr, after - before);
    }

    /// <summary>"before → after" preview for a spell stat (percent unless it is a count stat).</summary>
    public static string FormatSpellStatBeforeAfter(ESpellStat stat, float before, float after)
    {
        bool percent = IsPercentStat(stat);
        string beforeStr = percent ? SignedPercent(before) : SignedFlat(before);
        string afterStr = percent ? SignedPercent(after) : SignedFlat(after);

        return Neutralize(beforeStr) + " → " + Colorize(afterStr, after - before);
    }

    // ----------------------------------------------------------------------------------
    // High-level formatting (the funnel every displayer should use)
    // ----------------------------------------------------------------------------------

    /// <summary>
    /// Formats a value with the proper percent/flat convention, a leading sign and a color.
    /// Use for stat panels (tab / summary / character shop) and modifiers alike.
    /// </summary>
    public static string FormatValue(float value, bool isPercentage, bool colorize = true)
    {
        string text = SignedValue(value, isPercentage);
        return colorize ? Colorize(text, value) : text;
    }

    /// <summary>Formats a character-stat modifier (+/- bonus) using the stat's percent/flat rule and color.</summary>
    public static string FormatModifier(ECharacterStat stat, float value, bool colorize = true)
        => FormatValue(value, IsPercentStat(stat), colorize);

    /// <summary>Formats a spell-stat modifier (+/- bonus) using the stat's percent/flat rule and color.</summary>
    public static string FormatSpellModifier(ESpellStat stat, float value, bool colorize = true)
        => FormatValue(value, IsPercentStat(stat), colorize);

    /// <summary>
    /// Formats a value for the auto-generated stat panels (stat tab / character shop) from the
    /// metadata carried by <see cref="UIStatAttribute"/>. The percent/flat rule is derived from
    /// <paramref name="stat"/> (single source of truth). When <paramref name="absolute"/> is true
    /// the raw value is shown unsigned (e.g. "100", "1.5/s"); otherwise the signed delta from
    /// <paramref name="neutralValue"/> is shown. Always colorized (green &gt;= 0, red &lt; 0).
    /// </summary>
    public static string FormatPanelStat(float rawValue, ECharacterStat stat, float neutralValue = 0f,
        bool absolute = false, string suffix = "", int decimals = 0)
    {
        if (absolute)
        {
            string numberFormat = decimals <= 0 ? "0" : "0." + new string('0', decimals);
            return Colorize(rawValue.ToString(numberFormat) + suffix, rawValue);
        }

        float delta = rawValue - neutralValue;
        return Colorize(SignedValue(delta, IsPercentStat(stat)) + suffix, delta);
    }
}
