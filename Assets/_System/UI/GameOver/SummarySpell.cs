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
        Label.text = StatsFormatUtils.Humanize(spellData.ID.ToString());
        LevelValue.text = activeSpell.Level.ToString();
        Icon.sprite = icon;

        // Total damage dealt is an absolute (always >= 0) achievement number: colored via the
        // central funnel like the other summary stats (positive -> green).
        DamageValue.text = StatsFormatUtils.Colorize(
            activeSpell.TotalDamageDealt.ToString("N0"), activeSpell.TotalDamageDealt);
    }

    // Summary items are display-only; pointer events are intentionally ignored (matches SummaryStat).
    public override void OnPointerEnter(PointerEventData eventData) { }

    public override void OnPointerClick(PointerEventData eventData) { }
}