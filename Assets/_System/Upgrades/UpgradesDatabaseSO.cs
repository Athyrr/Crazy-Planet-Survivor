using UnityEngine;

/// <summary>
/// How the "Populate" button fills an <see cref="UpgradesDatabaseSO"/>.
/// </summary>
public enum EUpgradeDatabasePopulateMode
{
    /// <summary>Curated pool (e.g. per-character spell pools). The Populate button does nothing.</summary>
    Manual,

    /// <summary>Auto-fill with every <see cref="StatUpgradeSO"/> in the project.</summary>
    AllStatUpgrades,

    /// <summary>Auto-fill with every <see cref="SpellUpgradeSO"/> in the project.</summary>
    AllSpellUpgrades
}

[CreateAssetMenu(fileName = "UpgradesDatabase", menuName = "Survivor/Databases/Upgrades")]
public class UpgradesDatabaseSO : ScriptableObject
{
    public UpgradeSO[] Upgrades;

    [Tooltip("How the Populate button fills this database.\n" +
             "Manual = curated pool (per-character spell pools) — Populate is a no-op.\n" +
             "AllStatUpgrades / AllSpellUpgrades = auto-fill with all matching upgrades in the project.")]
    public EUpgradeDatabasePopulateMode PopulateMode = EUpgradeDatabasePopulateMode.Manual;

#if UNITY_EDITOR
    [EasyButtons.Button("Populate (by mode)")]
    public void Populate()
    {
        switch (PopulateMode)
        {
            case EUpgradeDatabasePopulateMode.AllStatUpgrades:
                Upgrades = System.Array.ConvertAll(
                    DatabaseAutoPopulateUtils.FindAllAssets<StatUpgradeSO>(), u => (UpgradeSO)u);
                break;

            case EUpgradeDatabasePopulateMode.AllSpellUpgrades:
                Upgrades = System.Array.ConvertAll(
                    DatabaseAutoPopulateUtils.FindAllAssets<SpellUpgradeSO>(), u => (UpgradeSO)u);
                break;

            default:
                Debug.LogWarning(
                    $"[{name}] PopulateMode is Manual — this is a curated pool, nothing was populated. " +
                    "Set PopulateMode to AllStatUpgrades / AllSpellUpgrades to auto-fill.", this);
                return;
        }

        DatabaseAutoPopulateUtils.Save(this);
        Debug.Log($"[{name}] Populated {Upgrades.Length} upgrades ({PopulateMode}).", this);
    }
#endif
}