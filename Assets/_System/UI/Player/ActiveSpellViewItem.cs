using UnityEngine.UI;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// UI element for a spell unlocked and active during a run.
/// </summary>
public class ActiveSpellViewItem : UIViewItemBase
{
    public Image Icon;
    public Image Border;

    public void Refresh(SpellDataSO data, int databaseIndex, int level)
    {
        if (data == null)
            return;

        if (Icon)
            Icon.sprite = data.Icon;
    }

    // Plays a feedback when the spell is upgraded
    public void PlayUpgradeFeedback(int activeSpellLevel)
    {
    }


    public override void OnPointerEnter(PointerEventData eventData)
    { 
        // todo Spell detail tooltip
    }
}