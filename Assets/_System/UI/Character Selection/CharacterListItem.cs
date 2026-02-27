using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Represents a UI charatcer button in Character Slectiuon UI.
/// </summary>
public class CharacterListItem : MonoBehaviour
{
    public Image Icon;
    public TMP_Text Text;
    public Button Button;


    private CharacterSelectionUIController _controller;
    private CharacterSO _data;
    private int _index;

    private void OnEnable()
    {
        Button.onClick.AddListener(() => _controller.PreviewCharacter(_index));
    }

    private void OnDisable()
    {
        Button.onClick.RemoveAllListeners();
    }

    public void Init(CharacterSelectionUIController selectionController, int index, CharacterSO data)
    {
        _controller = selectionController;
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
}
