using Unity.Cinemachine;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Serialization;

public class LobbyManager : MonoBehaviour
{
    [Header("Cameras")] [Tooltip("Camera used to follow the player.")]
    public CinemachineCamera GameCamera;

    [Tooltip("Camera used for the planet selection view (Galaxy).")]
    public CinemachineCamera PlanetSelectionCamera;

    [Header("UI Controllers")] 
    public CharacterSelectionUIController CharacterSelectionUIController;
    public PlanetSelectionUIController PlanetSelectionUIController;
    public AmuletShopUIController AmuletShopUIController;

    private EntityManager _entityManager;
    private EntityQuery _openCharacterSelectionViewQuery;
    private EntityQuery _openPlanetSelectionViewQuery;
    private EntityQuery _openAmuletShopViewQuery;

    private void OnEnable()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnGameStateChanged += HandleStateChange;
    }

    private void OnDisable()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnGameStateChanged -= HandleStateChange;
    }


    private void Start()
    {
        _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;

        _openCharacterSelectionViewQuery = _entityManager.CreateEntityQuery(typeof(OpenCharactersViewRequest));
        _openPlanetSelectionViewQuery = _entityManager.CreateEntityQuery(typeof(OpenPlanetSelectionViewRequest));
        _openAmuletShopViewQuery = _entityManager.CreateEntityQuery(typeof(OpenAmuletShopViewRequest));
    }

    private void Update()
    {
        CheckOpenViewRequests();

        // Debug.Log("Current Game State: " + GameManager.Instance.GetGameState());
    }

    private void CheckOpenViewRequests()
    {
        // Check character selection view request
        if (!_openCharacterSelectionViewQuery.IsEmpty)
        {
            GameManager.Instance.ChangeState(EGameState.CharacterSelection);
            _entityManager.DestroyEntity(_openCharacterSelectionViewQuery);
        }

        // Check planet selection view request
        if (!_openPlanetSelectionViewQuery.IsEmpty)
        {
            GameManager.Instance.ChangeState(EGameState.PlanetSelection);
            _entityManager.DestroyEntity(_openPlanetSelectionViewQuery);
        }
        
        // Check amulet shop view request
        if (!_openAmuletShopViewQuery.IsEmpty)
        {
            GameManager.Instance.ChangeState(EGameState.AmuletShop);
            _entityManager.DestroyEntity(_openAmuletShopViewQuery);
        }
    }

    private void HandleStateChange(EGameState newState)
    {
        PlanetSelectionUIController.gameObject.SetActive(false);
        CharacterSelectionUIController.gameObject.SetActive(false);
        AmuletShopUIController.gameObject.SetActive(false);

        bool isGalaxyMode = (newState == EGameState.PlanetSelection);
        PlanetSelectionCamera.Priority = isGalaxyMode ? 10 : -1;

        switch (newState)
        {
            case EGameState.Lobby:
                break;
            case EGameState.CharacterSelection:
                OpenCharacterSelectionView();
                break;
            case EGameState.PlanetSelection:
                OpenPlanetSelectionView();
                break;
            case EGameState.AmuletShop:
                OpenAmuletShopView();
                break;
        }
    }

    private void OpenCharacterSelectionView()
    {
        CharacterSelectionUIController.gameObject.SetActive(true);
        CharacterSelectionUIController.OpenView();
    }

    private void OpenPlanetSelectionView()
    {
        PlanetSelectionUIController.gameObject.SetActive(true);
        PlanetSelectionUIController.OpenView();

        // Active planet selection camera
        PlanetSelectionCamera.Priority = 20;
    }

    private void OpenAmuletShopView()
    {
        AmuletShopUIController.gameObject.SetActive(true);
        AmuletShopUIController.OpenView();
    }
}