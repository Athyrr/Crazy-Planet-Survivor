using Unity.Entities.Serialization;
using System.Collections;
using Unity.Entities;
using Unity.Scenes;
using UnityEngine;
using System;

[DefaultExecutionOrder(-100)]
public class GameManager : MonoBehaviour
{
    public delegate void GameStateChanged(EGameState newState);
    public event GameStateChanged OnGameStateChanged;

    public GameObject LoadingPanel;

    [SerializeField]
    private float _minLoadingTime = 1.0f;

    public static GameManager Instance
    {
        get;
        private set;
    }

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

    private void OnEnable()
    {
        OnGameStateChanged += HandleInternalStateChange;
    }

    private void HandleInternalStateChange(EGameState newState)
    {
        if (LoadingPanel != null)
            LoadingPanel.SetActive(newState == EGameState.Loading);
    }

    private void OnDisable()
    {
        OnGameStateChanged -= HandleInternalStateChange;
    }


    private void Start()
    {
        _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;

        _gameStateQuery = _entityManager.CreateEntityQuery(typeof(GameState));

        _planetScenesBufferQuery = _entityManager.CreateEntityQuery(typeof(PlanetSceneRefBufferElement));

        LoadSceneInternal(EPlanetID.Lobby);
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

        var gameState = _gameStateQuery.GetSingleton<GameState>();
        return gameState.State;
    }

    public void LoadPlanetSubScene(EPlanetID planet)
    {
        LoadSceneInternal(planet);

        //EGameState newState = planet == EPlanetID.Lobby ? EGameState.Lobby : EGameState.Running;
        //ChangeState(newState);
    }

    private void LoadSceneInternal(EPlanetID planetID)
    {
        if (_planetScenesBufferQuery.IsEmpty)
            return;

        var planetScenesBufferEntity = _planetScenesBufferQuery.GetSingletonEntity();
        var planetScenesBuffer = _entityManager.GetBuffer<PlanetSceneRefBufferElement>(planetScenesBufferEntity);

        EntitySceneReference targetSceneRef = default;
        bool found = false;

        foreach (var planetScene in planetScenesBuffer)
        {
            if (planetScene.PlanetID == planetID)
            {
                targetSceneRef = planetScene.SceneReference;
                found = true;
                break;
            }
        }

        if (!found)
        {
            Debug.LogError($"Scene not found for ID: {planetID}", this);
            return;
        }

        LoadingPanel.SetActive(true);

        EGameState targetState = (planetID == EPlanetID.Lobby) ? EGameState.Lobby : EGameState.Running;

        ChangeState(EGameState.Loading);

        StartCoroutine(LoadSceneCororoutine(targetSceneRef, targetState));

        Debug.Log($"[GameManager] Scene Loaded: {planetID}");
    }

    private IEnumerator LoadSceneCororoutine(EntitySceneReference sceneRef, EGameState targetState)
    {
        if (_currentSceneEntity != Entity.Null)
        {
            UnloadCurrentScene();
            yield return null;
        }

        _currentSceneEntity = SceneSystem.LoadSceneAsync(World.DefaultGameObjectInjectionWorld.Unmanaged, sceneRef);

        float timer = 0f;
        bool isLoaded = false;

        while (!isLoaded || timer < _minLoadingTime)
        {
            //isLoaded = SceneSystem.IsSceneLoaded(World.DefaultGameObjectInjectionWorld.Unmanaged, _currentSceneEntity);
            if (_entityManager.Exists(_currentSceneEntity))
                isLoaded = true;

            timer += Time.deltaTime;
            yield return null;
        }

        if (timer < _minLoadingTime)
        {
            float remainingTime = _minLoadingTime - timer;
            yield return new WaitForSeconds(remainingTime);
        }

        //LoadingPanel.SetActive(false);

        ChangeState(targetState);
    }

    private void UnloadCurrentScene()
    {
        if (_currentSceneEntity != Entity.Null)
        {
            SceneSystem.UnloadScene(World.DefaultGameObjectInjectionWorld.Unmanaged, _currentSceneEntity, SceneSystem.UnloadParameters.DestroyMetaEntities);
            _currentSceneEntity = Entity.Null;
        }
    }

    public void ReturnToLobby()
    {
        // Create request to clear run
        var entity = _entityManager.CreateEntity();
        _entityManager.AddComponentData(entity, new ClearRunRequest());

        LoadSceneInternal(EPlanetID.Lobby);
        //ChangeState(EGameState.Lobby);
    }

    public void Quit()
    {
        Application.Quit();
    }
}
