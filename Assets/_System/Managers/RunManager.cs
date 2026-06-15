using Unity.Entities;
using UnityEngine;
using UnityEngine.Serialization;

public class RunManager : MonoBehaviour
{
    [Header("UI Managers")] 
    public HUDController HUDController;
    public UpgradeSelectionUIController UpgradeSelectionController;
    public GameOverUIController GameOverUIController;

    public GameObject PausePanel;

    private EntityManager _entityManager;
    private EntityQuery _playerHealthQuery;
    private EntityQuery _endRunQuery;
    private EntityQuery _playerQuery;
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
        _openUpgradesRequestQuery =
            _entityManager.CreateEntityQuery(typeof(GameState), typeof(OpenUpgradesSelectionViewRequest));
        _gameStateQuery = _entityManager.CreateEntityQuery(typeof(GameState));
        _endRunQuery = _entityManager.CreateEntityQuery(typeof(EndRunRequest));
        _playerQuery = _entityManager.CreateEntityQuery(typeof(Player));

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

        if (!_endRunQuery.IsEmpty && !_playerQuery.IsEmpty)
        {
            var endRunReqEntity = _endRunQuery.GetSingletonEntity();
            var playerEntity = _playerQuery.GetSingletonEntity();

            var endRunRequest = _entityManager.GetComponentData<EndRunRequest>(endRunReqEntity);
            var playerResources = _entityManager.GetBuffer<ResourceBufferElement>(playerEntity);

            if (GameManager.Instance.GetGameState() != EGameState.GameOver)
                GameManager.Instance.ChangeState(EGameState.GameOver);

            var resourcesArray = new ResourceBufferElement[playerResources.Length];
            for (int i = 0; i < playerResources.Length; i++)
                resourcesArray[i] = playerResources[i];

            GameOverUIController.OpenView(endRunRequest.State, resourcesArray);

            _entityManager.DestroyEntity(endRunReqEntity);
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
        HUDController.gameObject.SetActive(false);
        UpgradeSelectionController.gameObject.SetActive(false);
        // Slide the pause panel out (then deactivate it) instead of an instant pop-off.
        CloseAnimatedPanel(PausePanel);
        GameOverUIController.gameObject.SetActive(false);

        switch (newState)
        {
            case EGameState.Running:
                HUDController.gameObject.SetActive(true);
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

    /// <summary>
    /// Slides a panel out via its <see cref="UISlidePanel"/> (then deactivates it), or deactivates it
    /// immediately when it has no slide animator. No-op if already inactive.
    /// </summary>
    private static void CloseAnimatedPanel(GameObject panel)
    {
        if (panel == null || !panel.activeSelf)
            return;

        if (panel.TryGetComponent<UISlidePanel>(out var slide))
            slide.Hide(() => panel.SetActive(false));
        else
            panel.SetActive(false);
    }
}