using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "GameUpgradesConfigSO", menuName = "Survivor/Config/Game Upgrades Config")]
public class GameUpgradesConfigSO : ScriptableObject
{
    [Tooltip("Global Stats Upgrades (Speed, Health...)")]
    public UpgradesDatabaseSO GameStatUpgradesDatabase;

    [Tooltip("All Character Specific Upgrades")]
    public UpgradesDatabaseSO[] CharacterSpellUpgradesDatabases;

    public List<UpgradeSO> GetFlattenedUpgrades()
    {
        List<UpgradeSO> list = new List<UpgradeSO>();

        // Stats Upgrades
        if (GameStatUpgradesDatabase != null)
        {
            foreach (var up in GameStatUpgradesDatabase.Upgrades)
                if (up != null) list.Add(up);
        }

        // Characters Upgrades
        if (CharacterSpellUpgradesDatabases != null)
        {
            foreach (var db in CharacterSpellUpgradesDatabases)
            {
                if (db != null)
                {
                    foreach (var up in db.Upgrades)
                        if (up != null && !list.Contains(up)) list.Add(up);
                }
            }
        }
        return list;
    }
}