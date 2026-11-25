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

    public Action<PlanetSO> OnPlanetSelected;

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

    [Header("Planets")]
    [Tooltip("Planets database baked into SC_Entity_Core by PF_Database_Planets (same asset). A planet's index in it " +
             "is its only key between ECS and managed code.")]
    [SerializeField]
    private PlanetsDatabaseSO _planets;

    /// <summary>The planets database (Lobby, Sandbox, playable planets).</summary>
    public PlanetsDatabaseSO Planets => _planets;

    public static GameManager Instance { get; private set; }

    private EntityManager _entityManager;
    private EntityQuery _gameStateQuery;
    private EntityQuery _planetsDatabaseQuery;
    private EntityQuery _currentPlanetQuery;
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
        // Wait until the baked planets database is available before loading the lobby.
        while (_planetsDatabaseQuery.IsEmpty)
        {
            yield return null;
        }

        RequestLoad(_planets != null ? _planets.Lobby : null, EGameState.Lobby, sendStartRequest: false);
    }

    private void OnEnable()
    {
        OnGameStateChanged += HandleInternalStateChange;

        _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        _gameStateQuery = _entityManager.CreateEntityQuery(typeof(GameState));
        _planetsDatabaseQuery = _entityManager.CreateEntityQuery(typeof(PlanetsDatabase));
        _currentPlanetQuery = _entityManager.CreateEntityQuery(typeof(CurrentPlanet));
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
            if (_planetDataQuery.HasSingleton<PlanetData>()
                && _currentPlanetQuery.TryGetSingleton<CurrentPlanet>(out var currentPlanet)
                && _planets != null)
            {
                PlanetSO planet = _planets.Get(currentPlanet.Index);
                if (planet != null)
                    OnPlanetSelected?.Invoke(planet);
            }
        }
    }

    /// <param name="skipIntro">No RunStarting countdown (sandbox): the run is Running as soon as the scene is ready.</param>
    /// <param name="clearCurrentRun">A run is in progress: clear its RunScope entities first, like ReturnToLobby.</param>
    public void StartRun(PlanetSO planet, bool skipIntro = false, bool clearCurrentRun = false)
    {
        if (clearCurrentRun)
        {
            var clearEntity = _entityManager.CreateEntity();
            _entityManager.AddComponentData(clearEntity, new ClearRunRequest());
        }

        RequestLoad(planet, EGameState.Running, sendStartRequest: true, skipIntro: skipIntro);
    }

    public void ReturnToLobby()
    {
        var entity = _entityManager.CreateEntity();
        _entityManager.AddComponentData(entity, new ClearRunRequest());

        RequestLoad(_planets != null ? _planets.Lobby : null, EGameState.Lobby, sendStartRequest: false);
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
    private void RequestLoad(PlanetSO planet, EGameState targetState, bool sendStartRequest, bool skipIntro = false)
    {
        int index = _planets != null ? _planets.IndexOf(planet) : -1;
        if (index < 0)
        {
            Debug.LogError($"[GameManager] Load refused: planet '{(planet != null ? planet.name : "null")}' is not in " +
                           "the planets database (GameManager > Planets).", this);
            return;
        }

        var entity = _entityManager.CreateEntity();
        _entityManager.AddComponentData(
            entity,
            new LoadSceneRequest
            {
                PlanetIndex = index,
                TargetState = targetState,
                SendStartRequest = sendStartRequest,
                MinScreenTime = _minLoadingTime,
                Timeout = _loadTimeout,
                RunStartDelay = skipIntro ? 0f : _runStartDelay,
            }
        );

        Debug.Log($"[GameManager] Load requested: {planet.name} (index {index})");
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
