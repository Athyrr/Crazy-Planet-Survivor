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

    [Tooltip("Camera used to zoom in on a specific planet.")]
    public CinemachineCamera PlanetFocusCamera;

    [FormerlySerializedAs("CharacterSelectionUIController")] [Header("UI Controllers")] public CharacterShopUIController characterShopUIController;
    public PlanetSelectionUIController PlanetSelectionUIController;
    public AmuletShopUIController amuletShopUIController;

    private EntityManager _entityManager;
    private EntityQuery _openCharacterSelectionViewQuery;
    private EntityQuery _openPlanetSelectionViewQuery;
    private EntityQuery _openAmuletShopViewQuery;

    private void OnEnable()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnGameStateChanged += HandleStateChange;

        if (PlanetSelectionUIController != null)
            PlanetSelectionUIController.OnPlanetSelected += HandlePlanetFocus;
    }

    private void OnDisable()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnGameStateChanged -= HandleStateChange;

        if (PlanetSelectionUIController != null)
            PlanetSelectionUIController.OnPlanetSelected -= HandlePlanetFocus;
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
        // Let galaxy opened
        PlanetSelectionUIController.gameObject.SetActive(true);

        characterShopUIController.gameObject.SetActive(false);
        amuletShopUIController.gameObject.SetActive(false);

        bool isGalaxyMode = (newState == EGameState.PlanetSelection);
        if (isGalaxyMode)
        {
            PlanetSelectionCamera.Priority = 10;
        }
        else
        {
            PlanetSelectionCamera.Priority = -1;
            PlanetFocusCamera.Priority = -10;
        }


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
        characterShopUIController.gameObject.SetActive(true);
        characterShopUIController.OpenView();
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
        amuletShopUIController.gameObject.SetActive(true);
        amuletShopUIController.OpenView();
    }

    private void HandlePlanetFocus(EPlanetID planetID, Transform planetTransform, Vector3 focusOffset)
    {
        if (planetID != EPlanetID.None && planetTransform != null)
        {
            PlanetFocusCamera.Follow = planetTransform;
            PlanetFocusCamera.LookAt = planetTransform;

            var followComponent = PlanetFocusCamera.GetComponent<CinemachineFollow>();
            if (followComponent != null)
            {
                followComponent.FollowOffset = focusOffset;
            }

            PlanetFocusCamera.Priority = 30;
        }
        else
        {
            PlanetFocusCamera.Priority = 0;
            PlanetFocusCamera.Follow = null;
            PlanetFocusCamera.LookAt = null;
        }

        //todo hide other planets when focus and show details UI
    }
}