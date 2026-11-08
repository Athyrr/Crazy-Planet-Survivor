using System.Collections.Generic;

/// <summary>
/// What a hovered upgrade card affects: database indices of the equipped spells and the character stats.
/// Pure data + resolver.
/// </summary>
public readonly struct UpgradeHighlightTargets
{
    public static readonly UpgradeHighlightTargets Empty =
        new UpgradeHighlightTargets(new HashSet<int>(), new HashSet<ECharacterStat>());

    public readonly HashSet<int> SpellIndices;
    public readonly HashSet<ECharacterStat> Stats;

    public UpgradeHighlightTargets(HashSet<int> spellIndices, HashSet<ECharacterStat> stats)
    {
        SpellIndices = spellIndices;
        Stats = stats;
    }

    /// <summary>Resolves the targets of <paramref name="upgrade"/> against the player's equipped spells.</summary>
    public static UpgradeHighlightTargets Resolve(ref UpgradeBlob upgrade, in UpgradeDisplayContext context)
    {
        var spells = new HashSet<int>();
        var stats = new HashSet<ECharacterStat>();

        switch (upgrade.UpgradeType)
        {
            case EUpgradeType.PlayerStat:
                for (int i = 0; i < upgrade.StatModifiers.Length; i++)
                    stats.Add(upgrade.StatModifiers[i].CharacterStat);
                // A global stat never highlights every spell: only a tag-gated stat targets spells.
                if (upgrade.RequiredSpellTag != ESpellTag.None)
                    AddSpellsWithTags(spells, upgrade.RequiredSpellTag, in context);
                break;

            case EUpgradeType.UpgradeSpell:
                if (upgrade.SpellID != ESpellID.None)
                {
                    if (context.TryGetSpellIndex(upgrade.SpellID, out int index) && IsEquipped(index, in context))
                        spells.Add(index);
                }
                else if (upgrade.SpellTags != ESpellTag.None)
                {
                    AddSpellsWithTags(spells, upgrade.SpellTags, in context);
                }

                break;
        }

        return new UpgradeHighlightTargets(spells, stats);
    }

    private static bool IsEquipped(int dbIndex, in UpgradeDisplayContext context)
    {
        if (context.ActiveSpells == null)
            return false;
        for (int i = 0; i < context.ActiveSpells.Count; i++)
            if (context.ActiveSpells[i].DatabaseIndex == dbIndex)
                return true;
        return false;
    }

    private static void AddSpellsWithTags(HashSet<int> result, ESpellTag mask, in UpgradeDisplayContext context)
    {
        if (context.ActiveSpells == null)
            return;

        var db = context.SpellsDatabase;
        for (int i = 0; i < context.ActiveSpells.Count; i++)
        {
            var active = context.ActiveSpells[i];
            // Current tags = authored | upgrade-added | cached final (FinalTags may be 0 before first calc).
            ESpellTag tags = active.AddedTags | active.FinalTags;
            if (db != null && db.Spells != null && active.DatabaseIndex >= 0 && active.DatabaseIndex < db.Spells.Length
                && db.Spells[active.DatabaseIndex] != null)
                tags |= db.Spells[active.DatabaseIndex].Tags;

            if ((tags & mask) != 0)
                result.Add(active.DatabaseIndex);
        }
    }
}
