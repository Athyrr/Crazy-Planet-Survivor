using System;
using Unity.Entities;
using UnityEngine;

public class RunManager : MonoBehaviour
{
    public GameObject RunCanvas;

    public GameObject HUDPanel;
    public UI_UpgradeSelectionComponent UpgradeSelectionPanel;
    public GameObject PausePanel;
    public GameObject GameOverPanel;

    private EntityManager _entityManager;

    private EntityQuery _playerHealthQuery;
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
        _openUpgradesRequestQuery = _entityManager.CreateEntityQuery(typeof(GameState), typeof(OpenUpgradesSelectionMenuRequest));
        _gameStateQuery = _entityManager.CreateEntityQuery(typeof(GameState));

        InitPanels();
    }

    private void Update()
    {
        CheckPlayerHealth();
        CheckOpenUpgradesRequest();
    }

    private void InitPanels()
    {
        UpgradeSelectionPanel.Init(this);
    }

    private void CheckPlayerHealth()
    {     // If player died
        if (!_playerHealthQuery.IsEmpty)
        {
            var playerHealth = _playerHealthQuery.GetSingleton<Health>();
            if (playerHealth.Value <= 0)
            {
                GameManager.Instance.ChangeState(EGameState.GameOver);
                return;
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
            UpgradeSelectionPanel.DisplaySelection(buffer);

            GameManager.Instance.ChangeState(EGameState.UpgradeSelection);

            _entityManager.RemoveComponent<OpenUpgradesSelectionMenuRequest>(gameStateEntity);
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
        UpgradeSelectionPanel.gameObject.SetActive(false);
        PausePanel.SetActive(false);
        GameOverPanel.SetActive(false);

        switch (newState)
        {
            case EGameState.Running:
                HUDPanel.SetActive(true);
                break;
            case EGameState.Paused:
                PausePanel.SetActive(true);
                break;
            case EGameState.GameOver:
                GameOverPanel.SetActive(true);
                break;
            case EGameState.UpgradeSelection:
                UpgradeSelectionPanel.gameObject.SetActive(true);
                break;
            default:
                break;
        }
    }
}
