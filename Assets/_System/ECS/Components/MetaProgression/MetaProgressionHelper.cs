using Unity.Entities;

/// <summary>
/// Burst-safe extension methods for MetaProgressionLevelElement buffers.
/// Does NOT contain save/load calls (those require managed code).
/// </summary>
public static class MetaProgressionHelper
{
    /// <summary>
    /// Returns the purchased level for the given stat, or 0 if not found.
    /// </summary>
    public static int GetLevel(this DynamicBuffer<MetaProgressionLevelElement> buffer, ECharacterStat stat)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            if (buffer[i].Stat == stat)
                return buffer[i].Level;
        }

        return 0;
    }

    /// <summary>
    /// Returns the total bonus for the given stat, or 0 if not found.
    /// </summary>
    public static float GetTotalBonusValue(this DynamicBuffer<MetaProgressionLevelElement> buffer, ECharacterStat stat)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            if (buffer[i].Stat == stat)
                return buffer[i].TotalBonus;
        }

        return 0f;
    }

    /// <summary>
    /// Sets the purchased level and total bonus for the given stat. Adds a new entry if not found.
    /// </summary>
    public static void SetLevel(this DynamicBuffer<MetaProgressionLevelElement> buffer, ECharacterStat stat, int level, float totalBonus)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            if (buffer[i].Stat == stat)
            {
                buffer[i] = new MetaProgressionLevelElement
                {
                    Stat = stat,
                    Level = level,
                    TotalBonus = totalBonus
                };
                return;
            }
        }

        buffer.Add(new MetaProgressionLevelElement
        {
            Stat = stat,
            Level = level,
            TotalBonus = totalBonus
        });
    }
}
