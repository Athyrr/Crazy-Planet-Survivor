using System;
using System.Collections.Generic;

/// <summary>
/// Helpers for turning an <see cref="ESpellTag"/> flag mask into the individual, human-readable
/// tag names shown on the upgrade cards (one chip per tag).
/// </summary>
public static class SpellTagUtils
{
    // Single-bit tag values (excludes None and the All catch-all), cached once.
    private static readonly ESpellTag[] _individualTags = BuildIndividualTags();

    private static ESpellTag[] BuildIndividualTags()
    {
        var list = new List<ESpellTag>();
        foreach (ESpellTag value in Enum.GetValues(typeof(ESpellTag)))
        {
            if (value == ESpellTag.None || value == ESpellTag.All)
                continue;

            // Keep only single-bit flags (power of two) so composite values never appear twice.
            uint bits = (uint)value;
            if ((bits & (bits - 1)) == 0)
                list.Add(value);
        }

        return list.ToArray();
    }

    /// <summary>
    /// Fills <paramref name="results"/> (cleared first) with the readable name of every single-bit
    /// tag set in <paramref name="tags"/>, e.g. <c>Ranged | Burn</c> -> "Ranged", "Burn".
    /// </summary>
    public static void GetTagNames(ESpellTag tags, List<string> results)
    {
        results.Clear();
        if (tags == ESpellTag.None)
            return;

        for (int i = 0; i < _individualTags.Length; i++)
        {
            var tag = _individualTags[i];
            if ((tags & tag) != 0)
                results.Add(StatsFormatUtils.Humanize(tag.ToString()));
        }
    }

    /// <summary>True when at least one single-bit tag is set.</summary>
    public static bool HasAnyTag(ESpellTag tags) => (tags & ~ESpellTag.None) != 0;
}
