using System.Text.RegularExpressions;

/// <summary>
/// Centralized formatting helpers for displaying stats and stat modifiers in the UI.
/// Keeps sign/percent/color conventions consistent across the upgrade cards, character
/// shop, stat tab and game-over summary.
/// </summary>
public static class StatsFormatUtils
{
    /// <summary>Rich-text color for positive (bonus) values.</summary>
    public const string PositiveColor = "#4ADE80";

    /// <summary>Rich-text color for negative (malus) values.</summary>
    public const string NegativeColor = "#F87171";

    /// <summary>Inserts spaces before inner capitals: "MaxHealth" -> "Max Health".</summary>
    public static string Humanize(string camelCase)
    {
        if (string.IsNullOrEmpty(camelCase))
            return camelCase;

        return Regex.Replace(camelCase, "(\\B[A-Z])", " $1");
    }

    /// <summary>Signed percentage from a ratio: 0.1 -> "+10%", -0.2 -> "-20%", 0 -> "0%".</summary>
    public static string SignedPercent(float ratio)
    {
        float pct = ratio * 100f;
        return (pct > 0f ? "+" : "") + pct.ToString("0") + "%";
    }

    /// <summary>Signed flat value: 3 -> "+3", -2 -> "-2", 0 -> "0".</summary>
    public static string SignedFlat(float value)
    {
        return (value > 0f ? "+" : "") + value.ToString("0.##");
    }

    /// <summary>
    /// Formats a spell/stat value as either a signed percentage or a signed flat number.
    /// </summary>
    public static string SignedValue(float value, bool isPercentage)
    {
        return isPercentage ? SignedPercent(value) : SignedFlat(value);
    }

    /// <summary>Wraps text in a rich-text color tag based on its leading +/- sign.</summary>
    public static string ColorizeBySign(string formatted)
    {
        if (string.IsNullOrEmpty(formatted))
            return formatted;

        if (formatted.StartsWith("+"))
            return $"<color={PositiveColor}>{formatted}</color>";

        if (formatted.StartsWith("-"))
            return $"<color={NegativeColor}>{formatted}</color>";

        return formatted;
    }
}
