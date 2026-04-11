using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Represents a base class for a focusable and selectable UI element.
/// </summary>
public abstract class UIViewItemBase : MonoBehaviour, IPointerEnterHandler, IPointerClickHandler
{
    // Set image, icon etc when created or activated
    // public abstract void Init();
    
    public virtual void SetFocus(bool isFocused) { }
    public virtual void SetSelected(bool isSelected) { }

    public abstract void OnPointerEnter(PointerEventData eventData);

    public abstract void OnPointerClick(PointerEventData eventData);
}