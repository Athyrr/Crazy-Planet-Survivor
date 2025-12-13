using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UI_CharacterContainer_Character : MonoBehaviour
{
    public Image Icon;

    public TMP_Text Text;

    public Button Button;

    private UI_CharacterSelection_CharactersContainer _container;

    private CharacterDataSO _character;

    public void Init(UI_CharacterSelection_CharactersContainer container)
    {
        _container = container;
    }

    public void Refresh(CharacterDataSO characterData)
    {
        Icon.sprite = characterData.Icon;
        Text.text = characterData.DisplayName;
    }

    private void OnEnable()
    {
        Button.onClick.AddListener(() => _container.SelectCharacter(_character));
    }

    private void OnDisable()
    {
        Button.onClick.RemoveListener(() => _container.SelectCharacter(_character));
    }
}
