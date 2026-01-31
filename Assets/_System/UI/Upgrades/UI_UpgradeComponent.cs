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
        //Label.text = upgradeData.DisplayName.ToString();
        //Description.text = upgradeData.Description.ToString();

        switch (upgradeData.UpgradeType)
        {
            case EUpgradeType.Stat:
                Label.text = "Stat";

                Data.text = $"{upgradeData.CharacterStat}: \n";

                switch (upgradeData.ModifierStrategy)
                {
                    case EStatModiferStrategy.Flat:
                        Data.text += $"+{upgradeData.Value}";

                        break;
                    case EStatModiferStrategy.Multiply:
                        Data.text += $"+{upgradeData.Value * 100 - 100}%";

                        break;
                    default:
                        break;
                }
                break;

            case EUpgradeType.UnlockSpell:
                Label.text = "Unlock spell";

                Data.text = $"{upgradeData.SpellID}";
                break;

            default:
                break;
        }
    }
}
