using Unity.Entities;
using UnityEngine;

/// <summary>
/// Represents the Character Selection panel that managed playable character to play.
/// </summary>
public class CharacterSelectionUIController : MonoBehaviour
{
    [Header("Characters database")]

    public CharactersDatabaseSO Database;

    [Header("UI Views")]

    public CharacterListView CharacterListView;
    public CharacterDetailViewComponent CharacterDetailView;
    public CharacterStatsViewComponent CharacterStatsView;

    private EntityManager _entityManager;

    private int _currentSelectedCharacterIndex = 0;

    private void Awake()
    {
        _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;

        InitUI();

        _currentSelectedCharacterIndex = 0;
    }

    private void InitUI()
    {
        CharacterListView.Init(this, Database);
    }

    public void OpenView()
    {
        CharacterListView.RefreshCharacters();
        PreviewCharacter(_currentSelectedCharacterIndex);
    }

    public void CloseView()
    {
        // hide children
        foreach (Transform child in transform)
            child.gameObject.SetActive(false);
    }

    public void PreviewCharacter(int index)
    {
        _currentSelectedCharacterIndex = index;
        var characterData = Database.Characters[index];

        // Refresh containers
        CharacterDetailView.Refresh(characterData);
        CharacterStatsView.Refresh(characterData.BaseStats);
    }

    public void ConfirmSelection()
    {
        ConfirmSelection(_currentSelectedCharacterIndex);
    }

    private void ConfirmSelection(int index)
    {
        var requestEntity = _entityManager.CreateEntity();
        _entityManager.AddComponentData(requestEntity, new SelectCharacterRequest
        {
            CharacterIndex = index
        });

        GameManager.Instance.ChangeState(EGameState.Lobby);

        Debug.Log($"Select Character Index {index}");
    }
}