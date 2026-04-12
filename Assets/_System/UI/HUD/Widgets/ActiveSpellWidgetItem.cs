using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// UI element for a spell unlocked and active during a run.
/// </summary>
public class ActiveSpellWidgetItem : UIViewItemBase
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

    public void PlayUpgradeFeedback(int activeSpellLevel)
    {
    }
    
    public override void OnPointerEnter(PointerEventData eventData)
    { 
        // todo Spell detail tooltip
    }

    public override void OnPointerClick(PointerEventData eventData)
    {
    }
}