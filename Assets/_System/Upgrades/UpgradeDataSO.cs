using UnityEngine;

[CreateAssetMenu(fileName = "NewUpgradeData", menuName = "Survivor/Upgrade/Upgrade Data")]
public class UpgradeDataSO : ScriptableObject
{
    public string DisplayName;

    [TextArea(2, 4)]
    public string Description;

    public EUpgradeType UpgradeType;

    public EStatID Stat;
    public EStatModiferStrategy ModifierStrategy;
    public float Value;

    public ESpellID SpellID;
}
