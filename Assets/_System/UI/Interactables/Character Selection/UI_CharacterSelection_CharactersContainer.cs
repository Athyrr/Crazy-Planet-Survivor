using UnityEngine;
using System;
using System.Collections.Generic;

/// <summary>
/// Component that display the list of playable characters.
/// </summary>
public class UI_CharacterSelection_CharactersContainer : MonoBehaviour
{
    public UI_CharacterContainer_Character CharacterUIPrefab;

    private CharactersDatabaseSO _characterDatabase;

    private List<UI_CharacterContainer_Character> _charactersUI = new();

    private UI_CharacterSelectionComponent _container;

    public void Init(UI_CharacterSelectionComponent container, CharactersDatabaseSO database)
    {
        _container = container;
        SetDatabase(database);
        RefreshCharacters();
    }

    public void Clear()
    {
        _charactersUI.Clear();
    }

    public void SetDatabase(CharactersDatabaseSO database)
    {
        _characterDatabase = database;
    }

    public void RefreshCharacters()
    {
        Clear();

        foreach (CharacterDataSO characterData in _characterDatabase.Characters)
        {
            UI_CharacterContainer_Character characterUI = Instantiate(CharacterUIPrefab, this.transform);
            characterUI.Init(this);
            characterUI.Refresh(characterData);
            _charactersUI.Add(characterUI);
        }
    }

    public void SelectCharacter(CharacterDataSO characterData)
    {
        _container.RefreshUI(characterData);
    }
}
