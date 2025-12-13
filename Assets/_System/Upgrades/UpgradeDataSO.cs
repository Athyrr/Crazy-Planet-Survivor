using UnityEngine;

[CreateAssetMenu(fileName = "NewUpgradeData", menuName = "Survivor/Upgrades/Upgrade Data")]
public class UpgradeDataSO : ScriptableObject
{
    public string DisplayName;

    [TextArea(2, 4)]
    public string Description;

    public EUpgradeType UpgradeType;

    public EStatID Stat;
    public EStatModiferStrategy ModifierStrategy;
    public float Value;

    //public string Extension = "%";

    public ESpellID SpellID;
}
