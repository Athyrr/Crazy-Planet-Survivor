using UnityEngine;

[CreateAssetMenu(fileName = "UpgradeDatabase", menuName = "Survivor/Upgrade/Upgrade Database")]
public class UpgradesDatabaseSO : ScriptableObject
{
    public UpgradeDataSO[] Upgrades;
}
