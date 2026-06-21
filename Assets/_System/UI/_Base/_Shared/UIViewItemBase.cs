using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Base class for a shop/grid UI element with an explicit, shared interaction-state model.
///
/// State layers (independent, combined by each item's own visual refresh):
/// <list type="bullet">
/// <item><b>Hovered</b> — pointer is over the item (PC mouse only). Highlight only, no detail panel.</item>
/// <item><b>Focused</b> — the navigation cursor / last clicked item. Highlight + shows the detail panel.</item>
/// <item><b>Selected</b> — committed: the purchase button is focused so the next confirm buys it.</item>
/// </list>
/// Content states (locked / maxed / unlocked) are handled by each concrete item.
/// </summary>
public abstract class UIViewItemBase
    : MonoBehaviour,
        IPointerEnterHandler,
        IPointerExitHandler,
        IPointerClickHandler
{
    /// <summary>Pointer hover (PC). Pure visual highlight — must not show details or commit.</summary>
    public virtual void SetHovered(bool isHovered) { }

    /// <summary>Navigation cursor / clicked item. Highlights and (via the controller) shows details.</summary>
    public virtual void SetFocus(bool isFocused) { }

    /// <summary>Committed item — the purchase button is focused.</summary>
    public virtual void SetSelected(bool isSelected) { }

    public abstract void OnPointerEnter(PointerEventData eventData);

    public virtual void OnPointerExit(PointerEventData eventData) { }

    public abstract void OnPointerClick(PointerEventData eventData);
}
