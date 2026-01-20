using TMPro;
using UnityEngine;

public class UI_UpgradeComponent : MonoBehaviour
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
                        Data.text = $"+{upgradeData.Value} Flat {upgradeData.CharacterStat}";

                        break;
                    case EStatModiferStrategy.Multiply:
                        Data.text = $"+{upgradeData.Value * 100 - 100}% {upgradeData.CharacterStat}";

                        break;
                    default:
                        break;
                }
                break;

            case EUpgradeType.UnlockSpell:
                Data.text = $"Unlock: {upgradeData.SpellID}";
                break;

            default:
                break;
        }
    }
}
