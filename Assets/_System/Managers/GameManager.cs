using Unity.Entities.Serialization;
using System.Collections;
using Unity.Entities;
using Unity.Scenes;
using UnityEngine;

[DefaultExecutionOrder(-100)]
public class GameManager : MonoBehaviour
{
    public delegate void GameStateChanged(EGameState newState);
    public event GameStateChanged OnGameStateChanged;

    public GameObject LoadingPanel;
    [SerializeField] private float _minLoadingTime = 1.0f;

    public static GameManager Instance { get; private set; }

    private EntityManager _entityManager;
    private EntityQuery _gameStateQuery;
    private EntityQuery _planetScenesBufferQuery;
    private Entity _currentSceneEntity = Entity.Null;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(this.gameObject);
    }

    private void Start()
    {
        _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        _gameStateQuery = _entityManager.CreateEntityQuery(typeof(GameState));
        _planetScenesBufferQuery = _entityManager.CreateEntityQuery(typeof(PlanetSceneRefBufferElement));

        // Loading Lobby
        InternalLoadScene(EPlanetID.Lobby, EGameState.Lobby, sendStartRequest: false);
    }

    private void OnEnable()
    {
        OnGameStateChanged += HandleInternalStateChange;
    }

    private void OnDisable()
    {
        OnGameStateChanged -= HandleInternalStateChange;
    }

    public void StartRun(EPlanetID planet)
    {
        InternalLoadScene(planet, EGameState.Running, sendStartRequest: true);
    }

    public void ReturnToLobby()
    {
        var entity = _entityManager.CreateEntity();
        _entityManager.AddComponentData(entity, new ClearRunRequest());

        InternalLoadScene(EPlanetID.Lobby, EGameState.Lobby, sendStartRequest: false);
    }

    public void Quit()
    {
        Application.Quit();
    }

    private void InternalLoadScene(EPlanetID planetID, EGameState targetState, bool sendStartRequest)
    {
        // Find scene ref
        if (!TryGetSceneReference(planetID, out EntitySceneReference sceneRef))
        {
            Debug.LogError($"[GameManager] Scene not found: {planetID}");
            return;
        }

        // Update game state
        ChangeState(EGameState.Loading);

        // Load scene
        StartCoroutine(LoadSceneCoroutine(sceneRef, targetState, sendStartRequest));

        Debug.Log($"[GameManager] Loading: {planetID}");
    }

    private bool TryGetSceneReference(EPlanetID planetID, out EntitySceneReference sceneRef)
    {
        sceneRef = default;

        if (_planetScenesBufferQuery.IsEmpty)
            return false;

        var bufferEntity = _planetScenesBufferQuery.GetSingletonEntity();
        var sceneRefsBuffer = _entityManager.GetBuffer<PlanetSceneRefBufferElement>(bufferEntity);

        foreach (var scene in sceneRefsBuffer)
        {
            if (scene.PlanetID == planetID)
            {
                sceneRef = scene.SceneReference;
                return true;
            }
        }
        return false;
    }

    private IEnumerator LoadSceneCoroutine(EntitySceneReference sceneRef, EGameState targetState, bool sendStartRequest)
    {
        // Unload
        if (_currentSceneEntity != Entity.Null)
        {
            SceneSystem.UnloadScene(World.DefaultGameObjectInjectionWorld.Unmanaged, _currentSceneEntity, SceneSystem.UnloadParameters.DestroyMetaEntities);
            _currentSceneEntity = Entity.Null;
            yield return null;
        }

        // Load async
        var worldUnmanaged = World.DefaultGameObjectInjectionWorld.Unmanaged;
        _currentSceneEntity = SceneSystem.LoadSceneAsync(worldUnmanaged, sceneRef);

        bool isLoaded = false;
        float timer = 0f;

        while (!isLoaded || timer < _minLoadingTime)
        {
            timer += Time.deltaTime;

            // Get load state
            var loadingState = SceneSystem.GetSceneStreamingState(worldUnmanaged, _currentSceneEntity);
            bool isStreamingDone = (loadingState == SceneSystem.SceneStreamingState.LoadedSuccessfully);

            bool isDataReady = true;
            if (sendStartRequest && isStreamingDone)
            {
                isDataReady = _entityManager.CreateEntityQuery(typeof(PlanetData)).HasSingleton<PlanetData>();
            }

            if (isStreamingDone && isDataReady)
                isLoaded = true;

            yield return null;
        }

        // Wait for physcis
        yield return new WaitForFixedUpdate();

        // Update state
        ChangeState(targetState);

        // Send Request if needed
        if (sendStartRequest)
        {
            var reqEntity = _entityManager.CreateEntity();
            _entityManager.AddComponent<StartRunRequest>(reqEntity);
            Debug.Log("[GameManager] StartRunRequest");
        }
    }

    public void ChangeState(EGameState newState)
    {
        if (!_gameStateQuery.IsEmpty)
        {
            var entity = _gameStateQuery.GetSingletonEntity();
            _entityManager.SetComponentData(entity, new GameState { State = newState });
        }

        OnGameStateChanged?.Invoke(newState);
    }

    public EGameState GetGameState()
    {
        if (_gameStateQuery.IsEmpty)
            return EGameState.Lobby;

        return _gameStateQuery.GetSingleton<GameState>().State;
    }

    private void HandleInternalStateChange(EGameState newState)
    {
        if (LoadingPanel != null)
            LoadingPanel.SetActive(newState == EGameState.Loading);
    }
}