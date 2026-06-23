using System.Collections.Generic;

/// <summary>
/// Runtime snapshot handed to the upgrade cards so they can display live data the static
/// <see cref="UpgradeBlob"/> does not carry:
/// <list type="bullet">
/// <item>the player's current <see cref="CoreStats"/> (for stat "before → after" previews),</item>
/// <item>a copy of the player's active spells (for the spell level + spell-stat "before → after"),</item>
/// <item>the <see cref="SpellDatabaseSO"/> (to map a spell id to its tags / database index).</item>
/// </list>
/// Built once per selection in <see cref="UpgradeSelectionUIController"/> and passed down to each card.
/// Everything is a value copy, so it stays valid after the originating EntityManager calls.
/// </summary>
public struct UpgradeDisplayContext
{
    public bool HasPlayerStats;
    public CoreStats PlayerStats;

    /// <summary>Copy of the player's ActiveSpell buffer (null if the player has no spells yet).</summary>
    public List<ActiveSpell> ActiveSpells;

    /// <summary>Managed spells database, indexed the same way as <see cref="ActiveSpell.DatabaseIndex"/>.</summary>
    public SpellDatabaseSO SpellsDatabase;

    /// <summary>Resolves the database index of the spell carrying <paramref name="id"/>.</summary>
    public bool TryGetSpellIndex(ESpellID id, out int index)
    {
        index = -1;
        if (id == ESpellID.None || SpellsDatabase == null || SpellsDatabase.Spells == null)
            return false;

        var spells = SpellsDatabase.Spells;
        for (int i = 0; i < spells.Length; i++)
        {
            if (spells[i] != null && spells[i].ID == id)
            {
                index = i;
                return true;
            }
        }

        return false;
    }

    /// <summary>Resolves the authored tags of the spell carrying <paramref name="id"/>.</summary>
    public bool TryGetSpellTags(ESpellID id, out ESpellTag tags)
    {
        tags = ESpellTag.None;
        if (!TryGetSpellIndex(id, out int index))
            return false;

        tags = SpellsDatabase.Spells[index].Tags;
        return true;
    }

    /// <summary>
    /// Resolves the authored description of the spell carrying <paramref name="id"/>.
    /// Used as the card text for spell unlocks (whose upgrade asset has no description of its own).
    /// </summary>
    public bool TryGetSpellDescription(ESpellID id, out string description)
    {
        description = string.Empty;
        if (!TryGetSpellIndex(id, out int index))
            return false;

        var spell = SpellsDatabase.Spells[index];
        if (spell == null || string.IsNullOrEmpty(spell.Description))
            return false;

        description = spell.Description;
        return true;
    }

    /// <summary>Finds the player's currently-owned active spell for <paramref name="id"/>, if any.</summary>
    public bool TryGetActiveSpell(ESpellID id, out ActiveSpell spell)
    {
        spell = default;
        if (ActiveSpells == null || !TryGetSpellIndex(id, out int index))
            return false;

        for (int i = 0; i < ActiveSpells.Count; i++)
        {
            if (ActiveSpells[i].DatabaseIndex == index)
            {
                spell = ActiveSpells[i];
                return true;
            }
        }

        return false;
    }
}
