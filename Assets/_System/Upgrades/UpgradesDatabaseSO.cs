using UnityEngine;

[CreateAssetMenu(fileName = "UpgradesDatabase", menuName = "Survivor/Databases/Upgrades")]
public class UpgradesDatabaseSO : ScriptableObject
{
    public UpgradeDataSO[] Upgrades;
}
