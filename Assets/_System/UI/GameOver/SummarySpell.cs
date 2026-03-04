using UnityEngine.UI;
using UnityEngine;
using TMPro;

public class SummarySpell : MonoBehaviour
{
    public TMP_Text Label;
    public Image Icon;
    public TMP_Text LevelValue;
    public TMP_Text DamageValue;

    public void Refresh(SpellBlob spellData, ActiveSpell activeSpell, Sprite icon)
    {
        Label.text = spellData.ID.ToString();
        LevelValue.text = activeSpell.Level.ToString();
        Icon.sprite = icon;

        DamageValue.text = activeSpell.DamageDealt.ToString("N0");
    }
}