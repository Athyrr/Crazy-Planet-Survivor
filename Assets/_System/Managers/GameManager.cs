using System;
using System.Collections;
using Unity.Entities;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

[DefaultExecutionOrder(-100)]
public class GameManager : MonoBehaviour
{
    public delegate void GameStateChanged(EGameState newState);

    public event GameStateChanged OnGameStateChanged;

    public Action<EPlanetID> OnPlanetSelected;

    public Canvas LoadingCanvas;

    public Canvas MainMenuCanvas;

    [SerializeField]
    private float _minLoadingTime = 1.0f;

    [Tooltip("Hard cap (seconds) before a stuck scene load bails out instead of hanging forever.")]
    [SerializeField]
    private float _loadTimeout = 30.0f;

    [Tooltip(
        "Intro delay (seconds) after the loading screen hides before a run actually starts. "
            + "The planet and player are shown during this window (arrival animation / intro text) "
            + "while enemies and the run timer stay frozen. 0 starts the run immediately."
    )]
    [SerializeField]
    private float _runStartDelay = 5.0f;

    [SerializeField]
    private Canvas _joystickCanvas;

    [Header("Performance")]
    [Tooltip(
        "Frames per second cap for the whole app. 60 for smooth mobile play. "
            + "Limited by the device screen's max refresh rate."
    )]
    [SerializeField]
    private int TargetFrameRate = 60;

    public static GameManager Instance { get; private set; }

    private EntityManager _entityManager;
    private EntityQuery _gameStateQuery;
    private EntityQuery _planetScenesBufferQuery;
    private EntityQuery _planetDataQuery;

    // Mirror of the ECS GameState. The scene loader (SceneLoadingSystem) mutates the GameState
    // singleton directly, so we poll it in Update and re-broadcast transitions to managed listeners.
    private EGameState _lastKnownState = (EGameState)(-1);

    [Space]
    [Header("Starting State \nChoose only MainMenu or PlanetSelection \n(others not tested)")]
    [SerializeField]
    private EGameState StartingState = EGameState.MainMenu;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(this.gameObject);
        Application.targetFrameRate = TargetFrameRate;
    }

    private void Start()
    {
        // Boot straight to the main menu. The lobby (and the rest of the run content) is only
        // streamed in once the player presses New Game (see StartNewGame).
        ChangeState(StartingState);
    }

    /// <summary>
    /// Entry point for the main menu's "New Game" button: streams the lobby planet scene and
    /// enters the Lobby state. Continue / saved games will hook in here later.
    /// </summary>
    public void StartNewGame()
    {
        StartCoroutine(WaitForSceneBufferCoroutine());
    }

    private IEnumerator WaitForSceneBufferCoroutine()
    {
        // Wait until the baked planet-scene reference buffer is available before loading the lobby.
        while (_planetScenesBufferQuery.IsEmpty)
        {
            yield return null;
        }

        RequestLoad(EPlanetID.Lobby, EGameState.Lobby, sendStartRequest: false);
    }

    private void OnEnable()
    {
        OnGameStateChanged += HandleInternalStateChange;

        _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        _gameStateQuery = _entityManager.CreateEntityQuery(typeof(GameState));
        _planetScenesBufferQuery = _entityManager.CreateEntityQuery(
            typeof(PlanetSceneRefBufferElement)
        );
        _planetDataQuery = _entityManager.CreateEntityQuery(typeof(PlanetData));
    }

    private void OnDisable()
    {
        OnGameStateChanged -= HandleInternalStateChange;
    }

    private void Update()
    {
        // The scene loader lives in ECS (SceneLoadingSystem) and drives the GameState singleton.
        // Detect those transitions here and re-broadcast them to managed listeners (audio, camera,
        // canvases...). Direct transitions via ChangeState keep _lastKnownState in sync so they are
        // not re-fired here.
        if (_gameStateQuery.IsEmpty)
            return;

        var current = _gameStateQuery.GetSingleton<GameState>().State;
        if (current == _lastKnownState)
            return;

        var previous = _lastKnownState;
        _lastKnownState = current;
        OnGameStateChanged?.Invoke(current);

        // A scene load just finished successfully (Loading -> gameplay state). MainMenu means the
        // load was aborted, so no planet was actually selected.
        if (previous == EGameState.Loading && current != EGameState.MainMenu)
        {
            if (_planetDataQuery.HasSingleton<PlanetData>())
                OnPlanetSelected?.Invoke(_planetDataQuery.GetSingleton<PlanetData>().PlanetID);
        }
    }

    public void StartRun(EPlanetID planet)
    {
        RequestLoad(planet, EGameState.Running, sendStartRequest: true);
    }

    public void ReturnToLobby()
    {
        var entity = _entityManager.CreateEntity();
        _entityManager.AddComponentData(entity, new ClearRunRequest());

        RequestLoad(EPlanetID.Lobby, EGameState.Lobby, sendStartRequest: false);
    }

    public void Quit()
    {
#if UNITY_EDITOR
        EditorApplication.ExitPlaymode();
#else
        Application.Quit();
#endif
    }

    /// <summary>
    /// Emit a scene-load request for the ECS <c>SceneLoadingSystem</c>, which owns the whole
    /// unload/stream/transition sequence. Concurrent requests are rejected by the system, so this
    /// is safe to call even while a load is in flight.
    /// </summary>
    private void RequestLoad(EPlanetID planetID, EGameState targetState, bool sendStartRequest)
    {
        var entity = _entityManager.CreateEntity();
        _entityManager.AddComponentData(
            entity,
            new LoadSceneRequest
            {
                PlanetID = planetID,
                TargetState = targetState,
                SendStartRequest = sendStartRequest,
                MinScreenTime = _minLoadingTime,
                Timeout = _loadTimeout,
                RunStartDelay = _runStartDelay,
            }
        );

        Debug.Log($"[GameManager] Load requested: {planetID}");
    }

    public void ChangeState(EGameState newState)
    {
        if (!_gameStateQuery.IsEmpty)
        {
            var entity = _gameStateQuery.GetSingletonEntity();
            _entityManager.SetComponentData(entity, new GameState { State = newState });
        }

        // Fire immediately for direct (non-load) transitions such as pause/unpause and shop exits,
        // and keep the mirror in sync so Update() does not re-fire the same transition.
        if (_lastKnownState != newState)
        {
            _lastKnownState = newState;
            OnGameStateChanged?.Invoke(newState);
        }
    }

    public EGameState GetGameState()
    {
        if (_gameStateQuery.IsEmpty)
            return EGameState.Lobby;

        return _gameStateQuery.GetSingleton<GameState>().State;
    }

    private void HandleInternalStateChange(EGameState newState)
    {
        if (LoadingCanvas != null)
            LoadingCanvas.gameObject.SetActive(newState == EGameState.Loading);

        if (_joystickCanvas != null)
            _joystickCanvas.gameObject.SetActive(
                newState == EGameState.Running || newState == EGameState.Lobby
            );

        if (MainMenuCanvas != null)
            MainMenuCanvas.gameObject.SetActive(newState == EGameState.MainMenu);
    }
}
