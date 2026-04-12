using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Represents a UI charatcer button in Character Slectiuon UI.
/// </summary>
public class CharacterShopViewItem : UIViewItemBase
{
    public Image Icon;
    public TMP_Text Text;
    public Button Button;

    private CharacterShopUIController _controller;
    private CharacterSO _data;
    private int _index;
    private bool _isUnlocked;

    private void OnEnable()
    {
        // Button.onClick.AddListener(() => _controller.DetailView.Refresh(_data, true);
    }

    private void OnDisable()
    {
        Button.onClick.RemoveAllListeners();
    }

    public void Init(CharacterShopUIController shopController, int index, CharacterSO data,
        bool isUnlocked)
    {
        _controller = shopController;
        _data = data;
        _index = index;
        _isUnlocked = isUnlocked;

        Button.onClick.RemoveAllListeners();
        Button.onClick.AddListener(() => _controller.SelectItem(_index));

        Refresh();
    }

    public void Refresh()
    {
        if (_data.Icon != null)
            Icon.sprite = _data.Icon;

        Text.text = _data.DisplayName;
    }

    public override void OnPointerEnter(PointerEventData eventData)
    {
        Debug.Log($"Focus {this.Text}");
        _controller.FocusItem(_index);
    }

    public override void OnPointerClick(PointerEventData eventData)
    {
        _controller.SelectItem(_index);
    }

    public override void SetFocus(bool isFocused)
    {
    }

    public override void SetSelected(bool isSelected)
    {
        // _controller.SelectItem(_index);
    }
}