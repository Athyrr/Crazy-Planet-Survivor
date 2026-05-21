using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Grid item for the meta-progression shop.
/// Displays the upgrade icon with 5 level pips (filled/empty).
/// Spell Brigade style progression indicator.
/// </summary>
public class MetaProgressionViewItem : UIViewItemBase
{
    [Header("Main")]
    [SerializeField] private Image _icon;
    [SerializeField] private TMP_Text _nameLabel;
    [SerializeField] private Button _button;

    [Header("Level Pips")]
    [SerializeField] private Image[] _pips;          // 5 pips, order: left-to-right
    [SerializeField] private Sprite _pipFilled;
    [SerializeField] private Sprite _pipEmpty;

    [Header("Colors")]
    [SerializeField] private Color _normalColor = Color.white;
    [SerializeField] private Color _focusedColor = new Color(1f, 0.84f, 0f);  // gold
    [SerializeField] private Color _maxedColor = new Color(0.2f, 1f, 0.2f);   // green

    private MetaProgressionController _controller;
    private int _databaseIndex;
    private int _currentLevel;
    private int _maxLevel;

    public void Init(MetaProgressionController controller, int index, MetaUpgradeSO data, int level)
    {
        _controller = controller;
        _databaseIndex = index;
        _currentLevel = level;
        _maxLevel = data.BonusPerLevel != null ? data.BonusPerLevel.Length : 5;

        if (_icon != null) _icon.sprite = data.Icon;
        if (_nameLabel != null) _nameLabel.text = data.DisplayName;

        // Button kept for visual states only — OnPointerClick/OnPointerEnter handle interactions
        if (_button != null)
            _button.interactable = false;

        RefreshPips();
    }

    public void RefreshLevel(int level)
    {
        _currentLevel = level;
        RefreshPips();
    }

    private void RefreshPips()
    {
        if (_pips == null) return;

        bool isMaxed = _currentLevel >= _maxLevel;

        for (int i = 0; i < _pips.Length; i++)
        {
            if (_pips[i] == null) continue;

            bool isFilled = i < _currentLevel;
            _pips[i].sprite = isFilled ? _pipFilled : _pipEmpty;
            _pips[i].color = isMaxed && isFilled ? _maxedColor : Color.white;
        }

        if (_icon != null)
            _icon.color = isMaxed ? _maxedColor : Color.white;
    }

    public override void SetFocus(bool isFocused)
    {
        // highlight border/glow when hovered
    }

    public override void SetSelected(bool isSelected)
    {
        // highlight when selected
    }

    public override void OnPointerEnter(PointerEventData eventData)
    {
        if (_controller != null)
            _controller.FocusItem(_databaseIndex);
    }

    public override void OnPointerClick(PointerEventData eventData)
    {
        if (_controller != null)
            _controller.SelectItem(_databaseIndex);
    }
}
