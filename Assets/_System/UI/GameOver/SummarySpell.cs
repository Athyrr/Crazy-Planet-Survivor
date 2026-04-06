using UnityEngine.UI;
using UnityEngine;
using TMPro;
using UnityEngine.EventSystems;

public class SummarySpell : UIViewItemBase
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

        DamageValue.text = activeSpell.TotalDamageDealt.ToString("N0");
    }

    public override void OnPointerEnter(PointerEventData eventData)
    {
        throw new System.NotImplementedException();
    }
}