using System;
using Unity.Entities;
using UnityEngine;

public class RunManager : MonoBehaviour
{
    [Header("UI Managers")]
    public GameObject HUDPanel;
    public UpgradeSelectionUIController UpgradeSelectionController;

    public GameOverUIController GameOverUIController;

    public GameObject PausePanel;

    private EntityManager _entityManager;
    private EntityQuery _playerHealthQuery;
    private EntityQuery _endRunQuery;
    private EntityQuery _openUpgradesRequestQuery;
    private EntityQuery _gameStateQuery;

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

        _playerHealthQuery = _entityManager.CreateEntityQuery(typeof(Player), typeof(Health));
        _openUpgradesRequestQuery = _entityManager.CreateEntityQuery(typeof(GameState), typeof(OpenUpgradesSelectionViewRequest));
        _gameStateQuery = _entityManager.CreateEntityQuery(typeof(GameState));
        _endRunQuery = _entityManager.CreateEntityQuery(typeof(EndRunRequest));

        InitPanels();
    }

    private void Update()
    {
        // CheckPlayerHealth();

        CheckEndRun();
        CheckOpenUpgradesRequest();
    }

    private void InitPanels()
    {
        UpgradeSelectionController.Init(this);
    }

    private void CheckEndRun()
    {
        if (GameManager.Instance.GetGameState() != EGameState.Running)
            return;

        if (!_endRunQuery.IsEmpty)
        {
            var reqEntity = _endRunQuery.GetSingletonEntity();
            var endRunRequest = _entityManager.GetComponentData<EndRunRequest>(reqEntity);

            if (GameManager.Instance.GetGameState() != EGameState.GameOver)
                GameManager.Instance.ChangeState(EGameState.GameOver);

            GameOverUIController.OpenView(endRunRequest.State);

            _entityManager.DestroyEntity(reqEntity);
        }
    }

    private void CheckPlayerHealth()
    {
        // If player died
        if (!_playerHealthQuery.IsEmpty)
        {
            var playerHealth = _playerHealthQuery.GetSingleton<Health>();
            if (playerHealth.Value <= 0)
            {
                var reqEntity = _entityManager.CreateEntity();
                _entityManager.AddComponentData(reqEntity, new EndRunRequest { State = EEndRunState.Death });

                //if (GameManager.Instance.GetGameState() != EGameState.GameOver)
                //    GameManager.Instance.ChangeState(EGameState.GameOver);
            }
        }
    }

    private void CheckOpenUpgradesRequest()
    {
        if (_gameStateQuery.IsEmpty)
            return;

        var gameStateEntity = _gameStateQuery.GetSingletonEntity();

        if (!_openUpgradesRequestQuery.IsEmpty)
        {
            var buffer = _entityManager.GetBuffer<UpgradeSelectionBufferElement>(gameStateEntity, true);

            GameManager.Instance.ChangeState(EGameState.UpgradeSelection);
            UpgradeSelectionController.DisplaySelection(buffer);

            _entityManager.RemoveComponent<OpenUpgradesSelectionViewRequest>(gameStateEntity);
        }
    }

    public void TogglePause()
    {
        var gameState = GameManager.Instance.GetGameState();

        switch (gameState)
        {
            case EGameState.UpgradeSelection:
                GameManager.Instance.ChangeState(EGameState.Running);
                break;
            case EGameState.Running:
                GameManager.Instance.ChangeState(EGameState.Paused);
                break;
            case EGameState.Paused:
                GameManager.Instance.ChangeState(EGameState.Running);
                break;
        }
    }

    private void HandleStateChange(EGameState newState)
    {
        // Hide all panels
        HUDPanel.SetActive(false);
        UpgradeSelectionController.gameObject.SetActive(false);
        PausePanel.SetActive(false);
        GameOverUIController.gameObject.SetActive(false);

        switch (newState)
        {
            case EGameState.Running:
                HUDPanel.SetActive(true);
                break;
            case EGameState.Paused:
                PausePanel.SetActive(true);
                break;
            case EGameState.GameOver:
                //GameOverUIController.gameObject.SetActive(true);
                //GameOverUIController.OpenView();
                break;
            case EGameState.UpgradeSelection:
                UpgradeSelectionController.gameObject.SetActive(true);
                break;
            default:
                break;
        }
    }
}
