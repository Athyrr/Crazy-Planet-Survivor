using _System.Settings;
using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Represents a UI character button in the Character Selection UI.
/// </summary>
public class CharacterShopViewItem : UIViewItemBase
{
    public Image Icon;
    public TMP_Text Text;
    public Button Button;

    [Tooltip("Optional border image using the element-outline material (like the amulet item). " +
             "Its _OutlineColor is driven by the shared CpBaseUISettings item-outline colors. " +
             "Leave empty to fall back to tinting the label only.")]
    [SerializeField] private Image _border;

    private static readonly int OutlineColorShaderProperty = Shader.PropertyToID("_OutlineColor");

    private CharacterShopUIController _controller;
    private CharacterSO _data;
    private int _index;
    private bool _isUnlocked;

    private bool _isHovered;
    private bool _isFocused;
    private bool _isSelected;

    private Tween _scaleTween;

    public void Init(CharacterShopUIController shopController, int index, CharacterSO data,
        bool isUnlocked)
    {
        _controller = shopController;
        _data = data;
        _index = index;
        _isUnlocked = isUnlocked;

        // The item receives pointer events directly; the button is kept only as a raycast target,
        // so it must not also trigger focus on click (would double-fire).
        if (Button != null)
            Button.onClick.RemoveAllListeners();

        // Clone the outline material so each item drives its own _OutlineColor (like the amulet item).
        if (_border != null && _border.material != null)
            _border.material = new Material(_border.material);

        _isHovered = false;
        _isFocused = false;
        _isSelected = false;

        // Pooled items can be reused while still scaled from a previous hover — reset instantly.
        if (_scaleTween.isAlive)
            _scaleTween.Stop();
        transform.localScale = Vector3.one;

        Refresh();
    }

    public void Refresh()
    {
        if (_data.Icon != null)
            Icon.sprite = _data.Icon;

        Text.text = _data.DisplayName;
        RefreshVisual();
    }

    // Pointer hover (PC): highlight only — does not show details.
    public override void OnPointerEnter(PointerEventData eventData)
    {
        if (_controller != null)
            _controller.HoverItem(_index);
    }

    public override void OnPointerExit(PointerEventData eventData)
    {
        if (_controller != null)
            _controller.UnhoverItem(_index);
    }

    // Click (PC): focus the item and show its details.
    public override void OnPointerClick(PointerEventData eventData)
    {
        if (_controller != null)
            _controller.FocusItem(_index);
    }

    public override void SetHovered(bool isHovered)
    {
        _isHovered = isHovered;
        RefreshVisual();
        RefreshScale();
    }

    public override void SetFocus(bool isFocused)
    {
        _isFocused = isFocused;
        RefreshVisual();
        RefreshScale();
    }

    public override void SetSelected(bool isSelected)
    {
        _isSelected = isSelected;
        RefreshVisual();
    }

    private bool IsHighlighted => _isFocused || _isHovered;

    private void RefreshScale()
    {
        if (_scaleTween.isAlive)
            _scaleTween.Stop();

        float target = IsHighlighted ? CpUISettings.HoverScale : 1f;
        _scaleTween = Tween.Scale(
            transform, target, CpUISettings.HoverDuration, CpUISettings.HoverEase, useUnscaledTime: true);
    }

    private void RefreshVisual()
    {
        if (Text != null)
            Text.color = (_isSelected || IsHighlighted)
                ? CpUISettings.ComplementaryColor
                : CpUISettings.ComplementaryColorOver;

        // Border outline color follows the shared item-outline settings (like the amulet item).
        if (_border != null && _border.material != null)
            _border.material.SetColor(OutlineColorShaderProperty,
                CpUISettings.GetItemOutlineColor(_isSelected, IsHighlighted, _isUnlocked));
    }
}
