using UnityEngine.UI;
using UnityEngine;
using TMPro;

/// <summary>
/// Represents the section that display the selected character base stats.
/// </summary>
public class CharacterStatsView : MonoBehaviour
{
    [Header("Stats Texts")]

    public TMP_Text HealthText;
    public TMP_Text DamageText;
    public TMP_Text SpeedText;
    public TMP_Text ArmorText;

    [Header("Spells")]
    public Image SpellIcon;

    public void Refresh(BaseStats stats)
    {

    }
}
