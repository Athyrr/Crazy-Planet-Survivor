using UnityEngine;

/// <summary>
/// A run upgrade that modifies a player character stat (Damage, Speed, MaxHealth...).
/// </summary>
[CreateAssetMenu(fileName = "NewStatUpgrade", menuName = "Survivor/Upgrades/Stat Upgrade")]
public class StatUpgradeSO : UpgradeSO
{
    [Header("Target Character Stat")] public ECharacterStat CharacterStat;

    private void OnValidate()
    {
        // A stat upgrade is always a PlayerStat upgrade.
        UpgradeType = EUpgradeType.PlayerStat;
    }
}
