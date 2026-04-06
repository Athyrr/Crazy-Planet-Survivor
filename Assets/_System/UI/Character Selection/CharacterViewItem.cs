using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Represents a UI charatcer button in Character Slectiuon UI.
/// </summary>
public class CharacterViewItem : UIViewItemBase
{
    public Image Icon;
    public TMP_Text Text;
    public Button Button;


    private CharacterSelectionShopUIController _controller;
    private CharacterSO _data;
    private int _index;

    private void OnEnable()
    {
        // Button.onClick.AddListener(() => _controller.DetailView.Refresh(_data, true);
    }

    private void OnDisable()
    {
        Button.onClick.RemoveAllListeners();
    }

    public void Init(CharacterSelectionShopUIController selectionShopController, int index, CharacterSO data)
    {
        _controller = selectionShopController;
        _data = data;
        _index = index;

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
        throw new System.NotImplementedException();
    }
}