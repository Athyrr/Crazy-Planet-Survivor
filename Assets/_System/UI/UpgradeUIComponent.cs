using TMPro;
using UnityEngine;

public class UpgradeUIComponent : MonoBehaviour
{
    public TMP_Text Label;
    public TMP_Text Description;
    public Sprite Icon;
    public TMP_Text Data;



    public void SetData(ref UpgradeBlob upgradeData)
    {
        Label.text = upgradeData.DisplayName.ToString();
        //Description.text = upgradeData.Description.ToString();


        switch (upgradeData.UpgradeType)
        {
            case EUpgradeType.Stat:
                switch (upgradeData.ModifierStrategy)
                {
                    case EStatModiferStrategy.Flat:
                        Data.text = $"+{upgradeData.Value} Flat {upgradeData.StatType}";

                        break;
                    case EStatModiferStrategy.Multiply:
                        Data.text = $"+{upgradeData.Value * 100 - 100}% {upgradeData.StatType}";

                        break;
                    default:
                        break;
                }
                break;

            case EUpgradeType.Spell:
                Data.text = $"Unlock: {upgradeData.SpellID}";
                break;

            default:
                break;
        }
    }
}
