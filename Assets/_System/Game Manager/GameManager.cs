using Unity.Entities;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    public GameObject UpgradesPanel;
    public GameObject PausePanel;
    public GameObject GameOverPanel;

    public UpgradeSelectionComponent UpgradeSelectionUI;

    private EntityManager _entityManager;
    private EntityQuery _gameStateQuery;
    private EntityQuery _playerHealthQuery;

    public bool SpacePressed;

    private void Awake()
    {
        DontDestroyOnLoad(this.gameObject);
    }

    void Start()
    {
        _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;

        _gameStateQuery = _entityManager.CreateEntityQuery(typeof(GameState));
        _playerHealthQuery = _entityManager.CreateEntityQuery(typeof(Player), typeof(Health));
    }

    private void Update()
    {
        if (_gameStateQuery.IsEmpty)
            return;

        var gameStateEntity = _gameStateQuery.GetSingletonEntity();
        var currentGameState = _gameStateQuery.GetSingleton<GameState>();

        switch (currentGameState.State)
        {
            case EGameState.Running:
                HandleRunningState(gameStateEntity);
                break;

            case EGameState.Paused:
                HandlePausedState(gameStateEntity);
                break;

            default:
                break;
        }

        Debug.Log("Current state: " + currentGameState.State);
    }

    private void HandleRunningState(Entity gameStateEntity)
    {
        // If upgrades buffer is fullfiled.
        var buffer = _entityManager.GetBuffer<UpgradeSelectionElement>(gameStateEntity, true);
        if (buffer.Length > 0)
        {
            UpgradeSelectionUI.DisplaySelection(buffer);

            ChangeState(gameStateEntity, EGameState.UpgradeSelection);
            return;
        }

        // If player died
        if (!_playerHealthQuery.IsEmpty)
        {
            var playerHealth = _playerHealthQuery.GetSingleton<Health>();
            if (playerHealth.Value <= 0)
            {
                ChangeState(gameStateEntity, EGameState.GameOver);
                return;
            }
        }
    }

    private void HandlePausedState(Entity gameStateEntity)
    {
        if (SpacePressed)
            ChangeState(gameStateEntity, EGameState.Running);
    }

    private void ChangeState(Entity gameStateEntity, EGameState newState)
    {
        _entityManager.SetComponentData(gameStateEntity, new GameState { State = newState });

        switch (newState)
        {
            case EGameState.Running:
                //Time.timeScale = 1f;
                UpgradesPanel.SetActive(false);
                PausePanel.SetActive(false);
                GameOverPanel.SetActive(false);
                break;

            case EGameState.Paused:
                UpgradesPanel.SetActive(false);
                PausePanel.SetActive(true);
                GameOverPanel.SetActive(false);
                break;

            case EGameState.UpgradeSelection:
                UpgradesPanel.SetActive(true);
                PausePanel.SetActive(false);
                GameOverPanel.SetActive(false);
                break;

            case EGameState.GameOver:
                UpgradesPanel.SetActive(false);
                PausePanel.SetActive(false);
                GameOverPanel.SetActive(true);
                break;

            default:
                UpgradesPanel.SetActive(false);
                PausePanel.SetActive(false);
                GameOverPanel.SetActive(false);
                break;
        }
    }

    public void TogglePause()
    {
        var gameStateEntity = _gameStateQuery.GetSingletonEntity();
        var currentGameState = _gameStateQuery.GetSingleton<GameState>();

        if (currentGameState.State == EGameState.Running)
        {
            ChangeState(gameStateEntity, EGameState.Paused);
        }
        else if (currentGameState.State == EGameState.Paused)
        {
            ChangeState(gameStateEntity, EGameState.Running);
        }
        else if (currentGameState.State == EGameState.UpgradeSelection)
        {
            Debug.Log("Toggle selection");
            ChangeState(gameStateEntity, EGameState.Running);
        }
    }

    public void OnPauseButtonPressed()
    {
        var gameStateEntity = _gameStateQuery.GetSingletonEntity();
        ChangeState(gameStateEntity, EGameState.Paused);
    }

    public void OnResumeButtonPressed()
    {
        var gameStateEntity = _gameStateQuery.GetSingletonEntity();
        ChangeState(gameStateEntity, EGameState.Running);
    }

}
