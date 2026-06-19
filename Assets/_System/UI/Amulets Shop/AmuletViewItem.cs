using _System.Settings;
using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class AmuletViewItem : UIViewItemBase
{
    [SerializeField] private Button _amuletButton;
    [SerializeField] private TMP_Text _label;
    [SerializeField] private Image _icon;
    [SerializeField] private Image _border;
    [Tooltip("Badge shown when this amulet is the currently equipped one.")]
    [SerializeField] private TMP_Text _equippedLabel;

    [SerializeField] private ResourceDatabaseSO _resourceDatabase;

    [SerializeField] private ResourceWidgetItem resourceComponent;
    [SerializeField] private GameObject _ressourceComponentParent;

    private AmuletShopUIController _controller;
    private int _databaseIndex;
    private bool _isUnlocked;
    private bool _isEquipped;
    private bool _isHovered;
    private bool _isFocused;
    private bool _isSelected;

    private Tween _scaleTween;

    private static readonly int BackgroundColorShaderProperty = Shader.PropertyToID("_BackgroundColor");
    private static readonly int OutlineColorShaderProperty = Shader.PropertyToID("_OutlineColor");

    public void Init(AmuletShopUIController controller, int index, AmuletSO amuletData, bool isUnlocked, bool isEquipped)
    {
        _controller = controller;
        _databaseIndex = index;
        _isUnlocked = isUnlocked;
        _isEquipped = isEquipped;

        _label.text = amuletData.DisplayName;
        _icon.sprite = amuletData.Icon;
        _ressourceComponentParent.SetActive(!isUnlocked);

        // The item receives pointer events directly (OnPointerEnter/Exit/Click); the button is kept
        // only as a raycast target, so it must not also trigger focus on click (would double-fire).
        if (_amuletButton != null)
            _amuletButton.onClick.RemoveAllListeners();

        _border.material = new Material(_border.material);

        _isHovered = false;
        _isFocused = false;
        _isSelected = false;

        // Pooled items can be reused while still scaled from a previous hover — reset instantly.
        if (_scaleTween.isAlive)
            _scaleTween.Stop();
        transform.localScale = Vector3.one;

        RefreshColor();
    }

    // Pointer hover (PC): highlight only — does not show details.
    public override void OnPointerEnter(PointerEventData eventData)
    {
        if (_controller != null)
            _controller.HoverItem(_databaseIndex);
    }

    public override void OnPointerExit(PointerEventData eventData)
    {
        if (_controller != null)
            _controller.UnhoverItem(_databaseIndex);
    }

    // Click (PC): focus the item and show its details.
    public override void OnPointerClick(PointerEventData eventData)
    {
        if (_controller != null)
            _controller.FocusItem(_databaseIndex);
    }

    public override void SetHovered(bool isHovered)
    {
        _isHovered = isHovered;
        RefreshColor();
        RefreshScale();
    }

    public override void SetFocus(bool isFocused)
    {
        _isFocused = isFocused;
        RefreshColor();
        RefreshScale();
    }

    public override void SetSelected(bool isSelected)
    {
        _isSelected = isSelected;
        RefreshColor();
    }

    private void RefreshColor()
    {
        _icon.enabled = _isUnlocked;

        if (_equippedLabel != null)
        {
            _equippedLabel.gameObject.SetActive(_isEquipped);
            _equippedLabel.color = CpUISettings.EquippedLabelColor;
        }

        _label.color = GetTextColor();

        if (_border.material != null)
        {
            _border.material.SetColor(OutlineColorShaderProperty, GetBorderColor());
            // Owned amulets get a lighter background (read as "acquired"); unbought stay dark.
            _border.material.SetColor(BackgroundColorShaderProperty,
                _isUnlocked ? CpUISettings.ItemBackgroundOwned : CpUISettings.ItemBackground);
        }
    }

    private void RefreshScale()
    {
        if (_scaleTween.isAlive)
            _scaleTween.Stop();

        float target = IsHighlighted ? CpUISettings.HoverScale : 1f;

        // Skip a no-op tween when already at the target scale (PrimeTween warns on equal end value).
        if (transform.localScale == Vector3.one * target)
            return;

        _scaleTween = Tween.Scale(
            transform, target, CpUISettings.HoverDuration, CpUISettings.HoverEase, useUnscaledTime: true);
    }

    private bool IsHighlighted => _isFocused || _isHovered;

    private Color GetBorderColor()
        => CpUISettings.GetItemOutlineColor(_isSelected, IsHighlighted, _isUnlocked);

    // Shop items share the common content-color resolver (Complementary scheme). Amulets are binary
    // (owned / locked) with no maxed state, and "locked" here only means "not yet bought" — the name
    // is never greyed — so maxed/locked are passed false and only the active / idle colors apply.
    private Color GetTextColor()
        => CpUISettings.GetItemContentColor(_isSelected || IsHighlighted, isMaxed: false, isLocked: false);
}
