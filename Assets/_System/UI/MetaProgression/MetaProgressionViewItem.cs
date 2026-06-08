using _System.Settings;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Grid item for the meta-progression shop.
/// Displays the upgrade icon with level pips (filled/empty) and reacts to interaction
/// (hover / selected) and content (locked / maxed) states via sprite-swap + tint.
/// </summary>
public class MetaProgressionViewItem : UIViewItemBase
{
    [Header("Main")]
    [SerializeField] private Image _icon;
    [SerializeField] private TMP_Text _nameLabel;
    [SerializeField] private Button _button;

    [Header("State Graphic (optional)")]
    [Tooltip("Background/border image used for the hover / selected / locked sprite-swap and tint. " +
             "Leave empty to fall back to tinting the icon + label only.")]
    [SerializeField] private Image _stateGraphic;

    [Header("Outline (optional)")]
    [Tooltip("Border image using the element-outline material (like the amulet item). " +
             "Its _OutlineColor is driven by the shared CpBaseUISettings item-outline colors.")]
    [SerializeField] private Image _border;

    [Header("Level Pips")]
    [SerializeField] private Image[] _pips;          // 5 pips, order: left-to-right
    [SerializeField] private Sprite _pipFilled;
    [SerializeField] private Sprite _pipEmpty;

    [Header("State Sprites (optional, applied to State Graphic)")]
    [SerializeField] private Sprite _normalSprite;
    [SerializeField] private Sprite _hoverSprite;
    [SerializeField] private Sprite _selectedSprite;
    [SerializeField] private Sprite _lockedSprite;

    [Header("State Colors")]
    [SerializeField] private Color _normalColor = Color.white;
    [SerializeField] private Color _focusedColor = new Color(1f, 0.84f, 0f);   // gold (hover)
    [SerializeField] private Color _selectedColor = new Color(1f, 0.55f, 0f);  // orange (committed)
    [SerializeField] private Color _lockedColor = new Color(0.45f, 0.45f, 0.45f); // greyed (can't afford)
    [SerializeField] private Color _maxedColor = new Color(0.2f, 1f, 0.2f);    // green (maxed)

    private static readonly int OutlineColorShaderProperty = Shader.PropertyToID("_OutlineColor");

    private MetaProgressionController _controller;
    private int _databaseIndex;
    private int _currentLevel;
    private int _maxLevel;

    private bool _isHovered;   // pointer over (PC) — highlight only
    private bool _isFocused;   // navigation cursor / clicked item — highlight + details
    private bool _isSelected;  // committed — purchase button focused
    private bool _isLocked;    // cannot afford the next level

    public int DatabaseIndex => _databaseIndex;

    private bool IsMaxed => _currentLevel >= _maxLevel;

    public void Init(MetaProgressionController controller, int index, MetaUpgradeSO data, int level, bool canAfford)
    {
        _controller = controller;
        _databaseIndex = index;
        _currentLevel = level;
        _maxLevel = data.BonusPerLevel != null ? data.BonusPerLevel.Length : 5;

        if (_icon != null) _icon.sprite = data.Icon;
        if (_nameLabel != null) _nameLabel.text = data.DisplayName;

        // Button kept for layout only — OnPointerClick/OnPointerEnter handle interactions.
        if (_button != null)
            _button.interactable = false;

        // Clone the outline material so each item drives its own _OutlineColor (like the amulet item).
        if (_border != null && _border.material != null)
            _border.material = new Material(_border.material);

        _isHovered = false;
        _isFocused = false;
        _isSelected = false;
        _isLocked = !canAfford && !IsMaxed;

        RefreshPips();
        RefreshVisualState();
    }

    public void RefreshLevel(int level, bool canAfford)
    {
        _currentLevel = level;
        _isLocked = !canAfford && !IsMaxed;

        RefreshPips();
        RefreshVisualState();
    }

    /// <summary>Updates only the affordability (locked) state, e.g. after a purchase changed resources.</summary>
    public void SetAffordable(bool canAfford)
    {
        _isLocked = !canAfford && !IsMaxed;
        RefreshVisualState();
    }

    private void RefreshPips()
    {
        if (_pips == null) return;

        bool isMaxed = IsMaxed;

        for (int i = 0; i < _pips.Length; i++)
        {
            if (_pips[i] == null) continue;

            bool isFilled = i < _currentLevel;
            _pips[i].sprite = isFilled ? _pipFilled : _pipEmpty;
            _pips[i].color = isMaxed && isFilled ? _maxedColor : Color.white;
        }
    }

    public override void SetHovered(bool isHovered)
    {
        _isHovered = isHovered;
        RefreshVisualState();
    }

    public override void SetFocus(bool isFocused)
    {
        _isFocused = isFocused;
        RefreshVisualState();
    }

    public override void SetSelected(bool isSelected)
    {
        _isSelected = isSelected;
        RefreshVisualState();
    }

    /// <summary>
    /// Drives the visual feedback. Interaction states (selected > hover) take priority over the
    /// content states (maxed / locked) for the tint and the optional sprite-swap graphic.
    /// </summary>
    private void RefreshVisualState()
    {
        bool highlighted = _isFocused || _isHovered;
        Color stateColor = GetStateColor();

        // Optional background/border: sprite-swap + tint.
        if (_stateGraphic != null)
        {
            Sprite sprite = _normalSprite;
            if (_isSelected && _selectedSprite != null) sprite = _selectedSprite;
            else if (highlighted && _hoverSprite != null) sprite = _hoverSprite;
            else if (_isLocked && _lockedSprite != null) sprite = _lockedSprite;

            if (sprite != null)
                _stateGraphic.sprite = sprite;

            _stateGraphic.color = stateColor;
        }

        // Icon tint reflects the content state (maxed / locked) when not actively highlighted.
        if (_icon != null)
        {
            if (_isSelected || highlighted) _icon.color = Color.white;
            else if (IsMaxed) _icon.color = _maxedColor;
            else if (_isLocked) _icon.color = _lockedColor;
            else _icon.color = Color.white;
        }

        // Label follows the combined state color for readability.
        if (_nameLabel != null)
            _nameLabel.color = stateColor;

        // Border outline color follows the shared item-outline settings (like the amulet item).
        // Availability = affordable (locked = cannot afford the next level).
        if (_border != null && _border.material != null)
            _border.material.SetColor(OutlineColorShaderProperty,
                CpBaseUISettings.GetItemOutlineColor(_isSelected, highlighted, !_isLocked));
    }

    private Color GetStateColor()
    {
        if (_isSelected) return _selectedColor;
        if (_isFocused || _isHovered) return _focusedColor;
        if (IsMaxed) return _maxedColor;
        if (_isLocked) return _lockedColor;
        return _normalColor;
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

    // Click (PC): focus the item and show its details (the purchase button lives in the detail panel).
    public override void OnPointerClick(PointerEventData eventData)
    {
        if (_controller != null)
            _controller.FocusItem(_databaseIndex);
    }
}
