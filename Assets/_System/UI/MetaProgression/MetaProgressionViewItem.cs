using _System.Settings;
using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// I like eaating shit
/// Grid item for the meta-progression shop.
/// Shows the upgrade icon + name with a "level / max" text indicator, and reacts to interaction
/// (hover / focus / selected) and content (locked / maxed) states using the shared
/// outline + background model and colors from <see cref="CpUISettings"/> (like the amulet item).
/// </summary>
public class MetaProgressionViewItem : UIViewItemBase
{
    [Header("Main")]
    [SerializeField] private Image _icon;
    [SerializeField] private TMP_Text _nameLabel;
    [SerializeField] private Button _button;

    [Header("Outline / Background")]
    [Tooltip("Border image using the element-outline material. Its _OutlineColor and _BackgroundColor " +
             "follow the shared CpUISettings item colors (background lighter once maxed).")]
    [SerializeField] private Image _border;

    [Header("Level Pips")]
    [Tooltip("Container holding the generated level pips (e.g. a HorizontalLayoutGroup). One pip is " +
             "generated per max level: filled up to the current level, the next one previewed when focused.")]
    [SerializeField] private RectTransform _pipsContainer;
    [Tooltip("Template pip Image, cloned once per max level. Kept disabled; its clones are activated.")]
    [SerializeField] private Image _pipTemplate;

    private static readonly int OutlineColorShaderProperty = Shader.PropertyToID("_OutlineColor");
    private static readonly int BackgroundColorShaderProperty = Shader.PropertyToID("_BackgroundColor");

    private MetaProgressionController _controller;
    private int _databaseIndex;
    private int _currentLevel;
    private int _maxLevel;

    private Image[] _pipImages;

    private bool _isHovered;   // pointer over (PC) — highlight only
    private bool _isFocused;   // navigation cursor / clicked item — highlight + details
    private bool _isSelected;  // committed — purchase button focused
    private bool _isLocked;    // cannot afford the next level

    private Tween _scaleTween;

    public int DatabaseIndex => _databaseIndex;

    private bool IsMaxed => _currentLevel >= _maxLevel;
    private bool IsHighlighted => _isFocused || _isHovered;

    public void Init(MetaProgressionController controller, int index, MetaUpgradeSO data, int level, bool canAfford)
    {
        _controller = controller;
        _databaseIndex = index;
        _currentLevel = level;
        _maxLevel = data.BonusPerLevel != null ? data.BonusPerLevel.Length : 5;

        if (_icon != null) _icon.sprite = data.Icon;
        if (_nameLabel != null) _nameLabel.text = data.DisplayName;

        // Button kept for layout / raycast only — OnPointerClick/OnPointerEnter handle interactions.
        if (_button != null)
            _button.interactable = false;

        // Clone the outline material so each item drives its own _OutlineColor (like the amulet item).
        if (_border != null && _border.material != null)
            _border.material = new Material(_border.material);

        _isHovered = false;
        _isFocused = false;
        _isSelected = false;
        _isLocked = !canAfford && !IsMaxed;

        BuildPips();

        // Pooled items can be reused while still scaled from a previous hover — reset instantly.
        if (_scaleTween.isAlive)
            _scaleTween.Stop();
        transform.localScale = Vector3.one;

        RefreshVisualState();
    }

    public void RefreshLevel(int level, bool canAfford)
    {
        _currentLevel = level;
        _isLocked = !canAfford && !IsMaxed;
        RefreshVisualState();
    }

    /// <summary>Updates only the affordability (locked) state, e.g. after a purchase changed resources.</summary>
    public void SetAffordable(bool canAfford)
    {
        _isLocked = !canAfford && !IsMaxed;
        RefreshVisualState();
    }

    public override void SetHovered(bool isHovered)
    {
        _isHovered = isHovered;
        RefreshVisualState();
        RefreshScale();
    }

    public override void SetFocus(bool isFocused)
    {
        _isFocused = isFocused;
        RefreshVisualState();
        RefreshScale();
    }

    public override void SetSelected(bool isSelected)
    {
        _isSelected = isSelected;
        RefreshVisualState();
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

    /// <summary>
    /// Drives the visual feedback using the shared outline + background model. Interaction states
    /// (selected / highlighted) take priority over the content states (maxed / locked) for the tint.
    /// </summary>
    private void RefreshVisualState()
    {
        bool highlighted = IsHighlighted;
        Color contentColor = GetContentColor(highlighted);

        RefreshPips(highlighted);

        if (_nameLabel != null)
            _nameLabel.color = contentColor;

        if (_icon != null)
            _icon.color = CpUISettings.GetItemIconColor(_isSelected || highlighted, IsMaxed, _isLocked);

        // Border material drives both the outline color and the background fill.
        // Availability = affordable (locked = cannot afford the next level).
        // A maxed item gets a dedicated outline + a lighter "acquired" background — but interaction
        // (selected / highlighted) still outranks maxed for the outline.
        if (_border != null && _border.material != null)
        {
            _border.material.SetColor(OutlineColorShaderProperty,
                CpUISettings.GetItemOutlineColor(_isSelected, highlighted, !_isLocked, IsMaxed));
            _border.material.SetColor(BackgroundColorShaderProperty,
                IsMaxed ? CpUISettings.ItemBackgroundMaxed : CpUISettings.ItemBackground);
        }
    }

    /// <summary>
    /// Generates one pip per max level under <see cref="_pipsContainer"/> by cloning
    /// <see cref="_pipTemplate"/> (kept hidden). Called on Init; colors are driven by RefreshPips.
    /// </summary>
    private void BuildPips()
    {
        if (_pipsContainer == null || _pipTemplate == null)
        {
            _pipImages = null;
            return;
        }

        _pipTemplate.gameObject.SetActive(false);

        // Clear pips generated for a previous Init (pooled / rebuilt items).
        if (_pipImages != null)
        {
            for (int i = 0; i < _pipImages.Length; i++)
                if (_pipImages[i] != null)
                    Destroy(_pipImages[i].gameObject);
        }

        int pipLayer = _pipsContainer.gameObject.layer;

        _pipImages = new Image[_maxLevel];
        for (int i = 0; i < _maxLevel; i++)
        {
            var pip = Instantiate(_pipTemplate, _pipsContainer);
            pip.gameObject.layer = pipLayer;   // clones inherit the container's UI layer (template layer may differ)
            pip.gameObject.SetActive(true);
            _pipImages[i] = pip;
        }
    }

    /// <summary>
    /// Colors the pips: filled up to the current level, empty beyond. When the item is highlighted and
    /// affordable, the next pip (the one a purchase would fill) is shown in the preview color.
    /// Once fully maxed, every pip takes the dedicated <see cref="CpUISettings.PipMaxedColor"/>.
    /// </summary>
    private void RefreshPips(bool highlighted)
    {
        if (_pipImages == null)
            return;

        // Fully maxed: all pips read as "complete" with the dedicated maxed pip color.
        if (IsMaxed)
        {
            for (int i = 0; i < _pipImages.Length; i++)
                if (_pipImages[i] != null)
                    _pipImages[i].color = CpUISettings.PipMaxedColor;
            return;
        }

        bool previewNext = highlighted && !_isLocked;

        for (int i = 0; i < _pipImages.Length; i++)
        {
            if (_pipImages[i] == null)
                continue;

            if (i < _currentLevel)
                _pipImages[i].color = CpUISettings.PipFilledColor;
            else if (i == _currentLevel && previewNext)
                _pipImages[i].color = CpUISettings.PipPreviewColor;
            else
                _pipImages[i].color = CpUISettings.PipEmptyColor;
        }
    }

    // Shop items share the common content-color resolver. Meta-progression is the full case
    // (active > maxed > locked > idle); the binary amulet/character items reuse the same resolver
    // with maxed/locked = false.
    private Color GetContentColor(bool highlighted)
        => CpUISettings.GetItemContentColor(_isSelected || highlighted, IsMaxed, _isLocked);

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
