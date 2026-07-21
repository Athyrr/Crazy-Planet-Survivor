using System;
using System.Collections.Generic;
using System.Reflection;
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
/// <item>Positive values are shown in <see cref="PositiveColor"/> (green) with no leading '+';
/// negatives in <see cref="NegativeColor"/> (red) keeping their '-'.</item>
/// <item>Zero values are shown in <see cref="ZeroColor"/> (white).</item>
/// <item>Percentage stats always keep their '%', including 0% (which is white).</item>
/// <item>Health stats (MaxHealth / Health / HealthRegen) and count stats (Pierce / Bounce / Amount)
/// are always flat values (X / -X), never percentages.</item>
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

    /// <summary>Rich-text color for a zero value (white) — neither a bonus nor a malus.</summary>
    public static string ZeroColor => "#" + ColorUtility.ToHtmlStringRGB(Color.white);

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

    // Cached ECharacterStat -> UIStat display label, read once from the CoreStats fields.
    private static Dictionary<ECharacterStat, string> _statLabels;

    /// <summary>
    /// Display name for a character stat: the <see cref="UIStatAttribute.DisplayName"/> declared on the
    /// matching <see cref="CoreStats"/> field (humanized), or the humanized enum/property name when the
    /// stat carries no <see cref="UIStatAttribute"/>.
    /// </summary>
    public static string StatDisplayName(ECharacterStat stat)
    {
        _statLabels ??= BuildStatLabels();
        string label = _statLabels.TryGetValue(stat, out var l) && !string.IsNullOrEmpty(l)
            ? l
            : stat.ToString();
        return Humanize(label);
    }

    /// <summary>
    /// Display name for a spell stat. Reuses the character-stat label when an identically-named
    /// counterpart exists (so LifeStealChance / Damage / CritChance share one display-name source);
    /// otherwise falls back to the humanized property name.
    /// </summary>
    public static string StatDisplayName(ESpellStat stat)
    {
        if (Enum.TryParse<ECharacterStat>(stat.ToString(), out var charStat) && charStat != ECharacterStat.None)
            return StatDisplayName(charStat);

        return Humanize(stat.ToString());
    }

    /// <summary>Reads the UIStat display labels off the CoreStats fields into a lookup keyed by stat.</summary>
    private static Dictionary<ECharacterStat, string> BuildStatLabels()
    {
        var map = new Dictionary<ECharacterStat, string>();
        foreach (var field in typeof(CoreStats).GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            var attr = field.GetCustomAttribute<UIStatAttribute>();
            if (attr != null && attr.Stat != ECharacterStat.None)
                map[attr.Stat] = attr.DisplayName;
        }
        return map;
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

    /// <summary>Dash stats are absolute values (a charge count and a cooldown in seconds), never percentages.</summary>
    public static bool IsDashStat(ECharacterStat stat)
        => stat is ECharacterStat.DashCount or ECharacterStat.DashCooldown;

    /// <summary>Dash-effect stackables shown as flat numbers (force + chain damage).</summary>
    public static bool IsDashEffectFlatStat(ECharacterStat stat)
        => stat is ECharacterStat.DashKnockbackForce or ECharacterStat.DashKnockbackChain;

    /// <summary>Dash-effect one-shot unlocks (no numeric value row on the card).</summary>
    public static bool IsDashUnlockStat(ECharacterStat stat)
        => stat is ECharacterStat.DashKnockback or ECharacterStat.DashReflect;

    /// <summary>True when the character stat is displayed as a percentage (everything but health + counts + dash).</summary>
    public static bool IsPercentStat(ECharacterStat stat)
        => !IsHealthStat(stat) && !IsCountStat(stat) && !IsDashStat(stat) && !IsDashEffectFlatStat(stat);

    /// <summary>True when the spell stat is displayed as a percentage (everything but the count stats).</summary>
    public static bool IsPercentStat(ESpellStat stat)
        => stat is not (ESpellStat.Amount or ESpellStat.BounceCount or ESpellStat.PierceCount);

    // ----------------------------------------------------------------------------------
    // Low-level signed formatting
    // ----------------------------------------------------------------------------------

    /// <summary>Percentage from a ratio, no leading '+': 0.1 -> "10%", -0.2 -> "-20%", 0 -> "0" (no '%').</summary>
    public static string SignedPercent(float ratio)
    {
        string s = (ratio * 100f).ToString("0");
        if (s == "-0") s = "0"; // avoid a "-0%" from a tiny negative that rounds to zero
        return s == "0" ? s : s + "%"; // a zero value drops the '%' and shows just "0"
    }

    /// <summary>Flat value, no leading '+': 3 -> "3", -2 -> "-2", 0 -> "0".</summary>
    public static string SignedFlat(float value)
    {
        string s = value.ToString("0.##");
        if (s == "-0") s = "0";
        return s;
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
    /// Wraps text in a rich-text color tag based on the numeric value: green for &gt; 0,
    /// white for exactly 0, red for negatives. Prefer this when the numeric value is available.
    /// </summary>
    public static string Colorize(string text, float value)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        string color = value < 0f ? NegativeColor : value > 0f ? PositiveColor : ZeroColor;
        return $"<color={color}>{text}</color>";
    }

    /// <summary>
    /// Wraps text in a color tag based on its leading sign: a leading '-' is red, a value that reads
    /// as zero ("0", "0%", ...) is white, everything else is green. Use when only the formatted
    /// string is available; prefer <see cref="Colorize"/> when the numeric value is known.
    /// </summary>
    public static string ColorizeBySign(string formatted)
    {
        if (string.IsNullOrEmpty(formatted))
            return formatted;

        string color = formatted.StartsWith("-") ? NegativeColor
            : RepresentsZero(formatted) ? ZeroColor
            : PositiveColor;
        return $"<color={color}>{formatted}</color>";
    }

    /// <summary>True when the string's numeric part is entirely zeros (e.g. "0", "0%", "0.0/s").</summary>
    private static bool RepresentsZero(string formatted)
    {
        bool hasDigit = false;
        foreach (char c in formatted)
        {
            if (c >= '1' && c <= '9')
                return false;
            if (c == '0')
                hasDigit = true;
        }
        return hasDigit;
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
        bool absolute = IsHealthStat(stat) || stat == ECharacterStat.Luck || IsDashStat(stat);
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
