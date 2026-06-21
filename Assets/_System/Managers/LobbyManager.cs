using _System.Audio;
using Unity.Cinemachine;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Serialization;

public class LobbyManager : MonoBehaviour
{
    [Tooltip("Camera used for the planet selection view (Galaxy).")]
    public CinemachineCamera PlanetSelectionCamera;

    [Tooltip("Camera used to zoom in on a specific planet.")]
    public CinemachineCamera PlanetFocusCamera;

    [Header("UI Controllers")]
    public CharacterShopUIController CharacterShopUIController;
    public PlanetSelectionUIController PlanetSelectionUIController;
    public AmuletShopUIController AmuletShopUIController;
    public MetaProgressionController MetaProgressionController;

    private EntityManager _entityManager;
    private EntityQuery _openCharacterSelectionViewQuery;
    private EntityQuery _openPlanetSelectionViewQuery;
    private EntityQuery _openAmuletShopViewQuery;
    private EntityQuery _openMetaProgressionViewQuery;

    [Header("Galaxy 3D UI Controller")]
    [SerializeField]
    private GameObject _galaxy;

    [System.Serializable]
    private struct GalaxyPlanetPlacement
    {
        public EPlanetID PlanetID;

        [Tooltip("Local position the galaxy is moved to when a run starts on this planet.")]
        public Vector3 GalaxyPosition;

        [Tooltip(
            "Local rotation (euler angles) the galaxy is set to when a run starts on this planet."
        )]
        public Vector3 GalaxyRotation;
    }

    [Tooltip(
        "Per-planet galaxy placement applied when a run starts. "
            + "Each entry stores the galaxy local position & rotation for the selected planet."
    )]
    [SerializeField]
    private GalaxyPlanetPlacement[] _galaxyPlacements;

    // Galaxy "home" transform captured before the first run, restored when returning to the lobby.
    private Vector3 _galaxyHomePosition;
    private Vector3 _galaxyHomeRotation;
    private bool _galaxyHomeCaptured;

    // Planet selected for the current/last run, deactivated on run start and reactivated on return.
    private EPlanetID _selectedPlanetID = EPlanetID.None;
    private Transform _selectedPlanetTransform;
    private GameObject _deactivatedPlanet;

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

        _openCharacterSelectionViewQuery = _entityManager.CreateEntityQuery(
            typeof(OpenCharactersShopRequest)
        );
        _openPlanetSelectionViewQuery = _entityManager.CreateEntityQuery(
            typeof(OpenPlanetSelectionViewRequest)
        );
        _openAmuletShopViewQuery = _entityManager.CreateEntityQuery(typeof(OpenAmuletShopRequest));
        _openMetaProgressionViewQuery = _entityManager.CreateEntityQuery(
            typeof(OpenMetaProgressionShopRequest)
        );
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

        // Check meta progression shop view request
        if (!_openMetaProgressionViewQuery.IsEmpty)
        {
            GameManager.Instance.ChangeState(EGameState.MetaProgression);
            _entityManager.DestroyEntity(_openMetaProgressionViewQuery);
        }
    }

    private void HandleStateChange(EGameState newState)
    {
        // On the main menu no lobby content is streamed in yet: keep every lobby view hidden so
        // nothing shows (or steals input) behind the main menu canvas.
        if (newState == EGameState.MainMenu)
        {
            PlanetSelectionUIController.gameObject.SetActive(false);
            // Slide the lobby views out (then they deactivate themselves). No-op if already inactive.
            CharacterShopUIController.CloseAnimated();
            AmuletShopUIController.CloseAnimated();
            MetaProgressionController.CloseAnimated();
            return;
        }

        // Let galaxy opened
        PlanetSelectionUIController.gameObject.SetActive(true);

        // Slide out whichever lobby view is currently open (then it deactivates itself). The incoming
        // view (opened just below) slides in at the same time, reading as a cross-slide.
        CharacterShopUIController.CloseAnimated();
        AmuletShopUIController.CloseAnimated();
        MetaProgressionController.CloseAnimated();

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
                RestoreRunGalaxyState();
                break;
            case EGameState.Running:
                BeginRunGalaxyState();
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
            case EGameState.MetaProgression:
                OpenMetaProgressionView();
                break;
        }
    }

    private void OpenCharacterSelectionView()
    {
        CharacterShopUIController.gameObject.SetActive(true);
        CharacterShopUIController.OpenView();
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

    private void OpenMetaProgressionView()
    {
        if (MetaProgressionController != null)
        {
            MetaProgressionController.gameObject.SetActive(true);
            MetaProgressionController.OpenView();
        }
    }

    private void HandlePlanetFocus(
        EPlanetID planetID,
        Transform planetTransform,
        Vector3 focusOffset
    )
    {
        if (planetID != EPlanetID.None && planetTransform != null)
        {
            _selectedPlanetID = planetID;
            _selectedPlanetTransform = planetTransform;

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
            _selectedPlanetID = EPlanetID.None;
            _selectedPlanetTransform = null;

            PlanetFocusCamera.Priority = 0;
            PlanetFocusCamera.Follow = null;
            PlanetFocusCamera.LookAt = null;
        }
    }

    /// <summary>
    /// Called when a run starts (state becomes Running). Deactivates the selected planet and
    /// moves the galaxy to the placement configured for that planet.
    /// </summary>
    private void BeginRunGalaxyState()
    {
        // Capture the galaxy's home transform once, so we can restore it after the run.
        if (_galaxy != null && !_galaxyHomeCaptured)
        {
            _galaxyHomePosition = _galaxy.transform.localPosition;
            _galaxyHomeRotation = _galaxy.transform.localEulerAngles;
            _galaxyHomeCaptured = true;
        }

        // Deactivate the planet that was selected for this run.
        if (_selectedPlanetTransform != null)
        {
            _deactivatedPlanet = _selectedPlanetTransform.gameObject;
            _deactivatedPlanet.SetActive(false);
        }

        // Move the galaxy to the placement configured for the selected planet.
        if (_galaxy != null && TryGetGalaxyPlacement(_selectedPlanetID, out var placement))
        {
            _galaxy.transform.localPosition = placement.GalaxyPosition;
            _galaxy.transform.localEulerAngles = placement.GalaxyRotation;
        }
    }

    /// <summary>
    /// Called when returning to the lobby (state becomes Lobby). Restores the galaxy's home
    /// transform and reactivates the planet that was deactivated when the run started.
    /// </summary>
    private void RestoreRunGalaxyState()
    {
        // Restore the galaxy to its home transform.
        if (_galaxy != null && _galaxyHomeCaptured)
        {
            _galaxy.transform.localPosition = _galaxyHomePosition;
            _galaxy.transform.localEulerAngles = _galaxyHomeRotation;
        }

        // Reactivate the planet we deactivated when the run started.
        if (_deactivatedPlanet != null)
        {
            _deactivatedPlanet.SetActive(true);
            _deactivatedPlanet = null;
        }

        _selectedPlanetID = EPlanetID.None;
        _selectedPlanetTransform = null;
    }

    private bool TryGetGalaxyPlacement(EPlanetID planetID, out GalaxyPlanetPlacement placement)
    {
        if (_galaxyPlacements != null)
        {
            foreach (var entry in _galaxyPlacements)
            {
                if (entry.PlanetID == planetID)
                {
                    placement = entry;
                    return true;
                }
            }
        }

        placement = default;
        return false;
    }
}
