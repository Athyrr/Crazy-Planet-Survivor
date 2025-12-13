using Unity.Entities;
using UnityEngine;

public class UI_CharacterSelectionComponent : MonoBehaviour
{
    [Header("Data")]
    public CharactersDatabaseSO Database;

    [Header("UI panels")]
    public GameObject MainPanel;

    public UI_CharacterSelection_CharactersContainer CharactersContainerUI;
    public UI_CharacterSelection_SelectedContainer SelectedCharacterContainerUI;
    public UI_CharacterSelection_Stats SelectedCharacterStatsContainerUI;

    private EntityManager _entityManager;
    private EntityQuery _displayMenuQuery;
    private int _selectedCharacterIndex = 0;

    private GameManager _gameManager;

    private void Awake()
    {
        _gameManager = FindFirstObjectByType<GameManager>();
    }

    private void Start()
    {
        _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;

        InitUI();

        PreviewCharacter(0);

        MainPanel.SetActive(false);

        _displayMenuQuery = _entityManager.CreateEntityQuery(typeof(DisplayCharacterSelectionMenuRequest));
    }

    private void Update()
    {
        if (!MainPanel.activeSelf && !_displayMenuQuery.IsEmpty)
        {
            OpenMenu();

            _entityManager.DestroyEntity(_displayMenuQuery);
        }
    }

    public void OpenMenu()
    {
        MainPanel.SetActive(true);

        CharactersContainerUI.RefreshCharacters();
        PreviewCharacter(_selectedCharacterIndex);
        _gameManager.ChangeState(EGameState.CharacterSelection);
    }

    public void CloseMenu()
    {
        MainPanel.SetActive(false);
        _gameManager.ChangeState(EGameState.Lobby);
    }

    private void InitUI()
    {
        CharactersContainerUI.Init(this, Database);
    }

    private void OnCharacterButtonClicked(int index)
    {
        PreviewCharacter(index);
    }

    /// <summary>
    /// Previews the character and update UI.
    /// </summary>
    /// <param name="index"></param>
    public void PreviewCharacter(int index)
    {
        _selectedCharacterIndex = index;

        var data = Database.Characters[index];
        RefreshUI(data);
    }

    public void RefreshUI(CharacterDataSO data)
    {
        // On met à jour les stats affichées à droite
        // (Supposons que vos composants UI ont ces méthodes)
        // SelectedCharacterContainerUI.SetVisuals(data.Prefab); // Ou une image preview
        // SelectedCharacterStatsContainerUI.DisplayStats(data.BaseStats);

        Debug.Log($"UI: Selected {data.DisplayName}");
    }


    /// <summary>
    /// Selects the character to play with.
    /// </summary>
    /// <param name="index"></param>
    public void SelectCharacter(CharacterDataSO character)
    {
        var requestEntity = _entityManager.CreateEntity();
        _entityManager.AddComponentData(requestEntity, new SelectCharacterRequest
        {
            //CharacterIndex = index
        });
        CloseMenu();
    }
}