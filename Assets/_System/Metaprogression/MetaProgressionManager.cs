using System.Collections.Generic;
using Unity.Entities;

/// <summary>
/// Pure C# manager for meta-progression data.
/// Single source of truth for purchased upgrade levels.
/// Handles save/load and syncs to the ECS buffer for run-time application.
/// </summary>
public static class MetaProgressionManager
{
    private static Dictionary<ECharacterStat, int> _levels = new();

    /// <summary>
    /// Loads purchased levels from the Save file into memory.
    /// </summary>
    public static void LoadFromSave()
    {
        _levels.Clear();

        var save = SaveManager.GetCurrentSaveAs<Save>();
        if (save == null) return;

        var names = save.metaUpgrades.StatNames;
        var levels = save.metaUpgrades.Levels;

        if (names == null || levels == null || names.Length != levels.Length)
            return;

        for (int i = 0; i < names.Length; i++)
        {
            if (System.Enum.TryParse(names[i], out ECharacterStat stat))
            {
                _levels[stat] = levels[i];
            }
        }
    }

    /// <summary>
    /// Persists current levels to the Save file on disk.
    /// </summary>
    public static void SaveToDisk()
    {
        var save = SaveManager.GetCurrentSaveAs<Save>();
        if (save == null) return;

        save.metaUpgrades.StatNames = new string[_levels.Keys.Count];
        save.metaUpgrades.Levels = new int[_levels.Values.Count];

        int i = 0;
        foreach (var kvp in _levels)
        {
            save.metaUpgrades.StatNames[i] = kvp.Key.ToString();
            save.metaUpgrades.Levels[i] = kvp.Value;
            i++;
        }

        SaveManager.ManualSave();
    }

    /// <summary>
    /// Returns the purchased level for a stat (0 = not upgraded).
    /// </summary>
    public static int GetLevel(ECharacterStat stat)
    {
        return _levels.TryGetValue(stat, out int level) ? level : 0;
    }

    /// <summary>
    /// Sets the purchased level for a stat (does NOT auto-save).
    /// </summary>
    public static void SetLevel(ECharacterStat stat, int level)
    {
        _levels[stat] = level;
    }

    /// <summary>
    /// Returns all stat/level pairs currently in memory.
    /// </summary>
    public static Dictionary<ECharacterStat, int>.KeyCollection GetAllStats() => _levels.Keys;

    /// <summary>
    /// Writes current levels into the ECS MetaProgressionLevelElement buffer on GameState,
    /// computing TotalBonus from the provided database.
    /// Called at run start so ApplyMetaProgressionSystem can read it.
    /// </summary>
    public static void SyncToBuffer(MetaUpgradesDatabaseSO database)
    {
        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null) return;

        var em = world.EntityManager;
        var query = em.CreateEntityQuery(typeof(GameState));
        if (query.IsEmpty) return;

        var gameStateEntity = query.GetSingletonEntity();
        if (!em.HasBuffer<MetaProgressionLevelElement>(gameStateEntity)) return;

        var buffer = em.GetBuffer<MetaProgressionLevelElement>(gameStateEntity);
        buffer.Clear();

        foreach (var kvp in _levels)
        {
            if (kvp.Value <= 0) continue;

            var upgradeDef = database.GetUpgrade(kvp.Key);
            if (upgradeDef == null) continue;

            float totalBonus = upgradeDef.GetTotalBonus(kvp.Value);

            buffer.Add(new MetaProgressionLevelElement
            {
                Stat = kvp.Key,
                Level = kvp.Value,
                TotalBonus = totalBonus
            });
        }
    }
}
