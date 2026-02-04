using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Represents le container of all playables characters displayed in CHaracter Selection UI
/// </summary>
public class CharacterListView : MonoBehaviour
{
    [SerializeField]
    private CharacterListItem _characterUIPrefab;

    [SerializeField]
    private Transform _characterListContainer;

    private CharactersDatabaseSO _characterDatabase;
    private List<CharacterListItem> _characterList = new();
    private CharacterSelectionUIController _controller;

    #region Public API

    public void Init(CharacterSelectionUIController controller, CharactersDatabaseSO database)
    {       
        if (database == null || _characterDatabase == database)
            return;

        _characterDatabase = database;
        _controller = controller;
    }

    public void RefreshCharacters()
    {
        Clear();

        for (int i = 0; i < _characterDatabase.Characters.Length; i++)
        {
            var characterData = _characterDatabase.Characters[i];
            CharacterListItem characterListItem = Instantiate(_characterUIPrefab, _characterListContainer);
            characterListItem.Init(_controller, i, characterData);
            _characterList.Add(characterListItem);
        }
    }

    public void Clear()
    {
        foreach (var item in _characterList)
        {
            if (item == null)
                continue;

            Destroy(item.gameObject);
        }

        _characterList.Clear();
    }

    #endregion
}
