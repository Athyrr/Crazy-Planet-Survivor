using UnityEngine;

/// <summary>
/// Central database of all meta-progression upgrades.
/// Shared across all players and persisted between runs.
/// </summary>
[CreateAssetMenu(menuName = "Game/Meta Progression/Meta Upgrades Database")]
public class MetaUpgradesDatabaseSO : ScriptableObject
{
    [SerializeField] private MetaUpgradeSO[] _upgrades;

    public MetaUpgradeSO[] Upgrades => _upgrades;

    public MetaUpgradeSO GetUpgrade(ECharacterStat stat)
    {
        foreach (var upgrade in _upgrades)
        {
            if (upgrade.TargetStat == stat)
                return upgrade;
        }

        return null;
    }

    public int Count => _upgrades != null ? _upgrades.Length : 0;

#if UNITY_EDITOR
    [EasyButtons.Button("Populate (find all Meta Upgrades)")]
    public void Populate()
    {
        _upgrades = DatabaseAutoPopulateUtils.FindAllAssets<MetaUpgradeSO>();
        DatabaseAutoPopulateUtils.Save(this);
        Debug.Log($"[{name}] Populated {_upgrades.Length} meta upgrades.", this);
    }
#endif
}
