using Unity.Cinemachine;
using Unity.Entities;
using UnityEngine;

public class LobbyManager : MonoBehaviour
{
    [Header("Cameras")]

    [Tooltip("Camera used to follow the player.")]
    public CinemachineCamera GameCamera;

    [Tooltip("Camera used for the planet selection view (Galaxy).")]
    public CinemachineCamera PlanetSelectionCamera;

    public GameObject GalaxyContainer;

    [Header("UI Controllers")]

    public CharacterSelectionUIControllerComponent CharacterSelectionUIController;

    private EntityManager _entityManager;
    private EntityQuery _openCharacterSelectionViewQuery;
    private EntityQuery _openPlanetSelectionViewQuery;

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
    }

    private void Update()
    {
        CheckOpenViewRequests();

        Debug.Log("Current Game State: " + GameManager.Instance.GetGameState());
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
    }

    private void HandleStateChange(EGameState newState)
    {
        GalaxyContainer.SetActive(false);
        CharacterSelectionUIController.CloseView();


        bool isGalaxyMode = (newState == EGameState.PlanetSelection);
        bool isCharacterMode = (newState == EGameState.CharacterSelection);
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
        }
    }

    private void OpenCharacterSelectionView()
    {
        if (CharacterSelectionUIController.CharacterSelectionPanel.activeSelf)
            return;

        CharacterSelectionUIController.OpenView();
    }

    private void OpenPlanetSelectionView()
    {
        Debug.Log("[UI] Opening Planet Selection View.");

        if (!GalaxyContainer.activeSelf)
            GalaxyContainer.SetActive(true);

        // Active planet selection camera

        PlanetSelectionCamera.Priority = 20;
    }
}
