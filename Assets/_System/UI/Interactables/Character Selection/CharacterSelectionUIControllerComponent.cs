using Unity.Entities;
using UnityEngine;

/// <summary>
/// Represents the Character Selection panel that managed playable character to play.
/// </summary>
public class CharacterSelectionUIControllerComponent : MonoBehaviour
{
    [Header("Characters database")]

    public CharactersDatabaseSO Database;

    [Header("UI panels")]

    public GameObject CharacterSelectionPanel;

    public CharacterListView CharacterListView;
    public CharacterDetailViewComponent CharacterDetailView;
    public CharacterStatsViewComponent CharacterStatsView;

    private GameManager _gameManager;
    private EntityManager _entityManager;
    private EntityQuery _openMenuQuery;

    private int _currentSelectedCharacterIndex = 0;

    private void Awake()
    {
        _gameManager = FindFirstObjectByType<GameManager>();
    }

    private void Start()
    {
        _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;

        InitUI();

        _openMenuQuery = _entityManager.CreateEntityQuery(typeof(OpenCharactersMenuRequest));
        CharacterSelectionPanel.SetActive(false);

        _currentSelectedCharacterIndex = 0;
    }

    private void Update()
    {
        // @todo if not in lobby do not update


        // Detect display selection menu request from ECS systerm
        if (!CharacterSelectionPanel.activeSelf && !_openMenuQuery.IsEmpty)
        {
            OpenMenu();
            _entityManager.DestroyEntity(_openMenuQuery);
        }
    }

    private void InitUI()
    {
        CharacterListView.Init(this, Database);
    }

    public void OpenMenu()
    {
        CharacterSelectionPanel.SetActive(true);

        _gameManager.ChangeState(EGameState.CharacterSelection);

        CharacterListView.RefreshCharacters();
        PreviewCharacter(_currentSelectedCharacterIndex);
    }

    public void CloseMenu()
    {
        CharacterSelectionPanel.SetActive(false);
        _gameManager.ChangeState(EGameState.Lobby);
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
        CloseMenu();
    }

    private void ConfirmSelection(int index)
    {
        var requestEntity = _entityManager.CreateEntity();
        _entityManager.AddComponentData(requestEntity, new SelectCharacterRequest
        {
            CharacterIndex = index
        });

        Debug.Log($"Select Character Index {index}");
    }
}