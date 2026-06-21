using _System.ECS.Components.Audio;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Serialization;

public class RunManager : MonoBehaviour
{
    [Header("UI Managers")]
    public HUDController HUDController;
    public UpgradeSelectionUIController UpgradeSelectionController;
    public GameOverUIController GameOverUIController;

    [Tooltip(
        "Boss name card (disabled by default). Activated + played when the final boss appears."
    )]
    public BossAppearMessageUI BossAppearMessage;

    public GameObject PausePanel;

    private EntityManager _entityManager;
    private EntityQuery _playerHealthQuery;
    private EntityQuery _endRunQuery;
    private EntityQuery _playerQuery;
    private EntityQuery _openUpgradesRequestQuery;
    private EntityQuery _gameStateQuery;
    private EntityQuery _finalBossQuery;
    private EntityQuery _audioPlayerQuery;

    // True once the current final boss's appear-message has been played (reset when no boss is present).
    private bool _bossAnnounced;

    // Bumped on each state change so a hide-completion can tell whether its transition is still current.
    private int _transitionGen;

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
        _openUpgradesRequestQuery = _entityManager.CreateEntityQuery(
            typeof(GameState),
            typeof(OpenUpgradesSelectionViewRequest)
        );
        _gameStateQuery = _entityManager.CreateEntityQuery(typeof(GameState));
        _endRunQuery = _entityManager.CreateEntityQuery(typeof(EndRunRequest));
        _playerQuery = _entityManager.CreateEntityQuery(typeof(Player));
        _finalBossQuery = _entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<FinalBossTag>(),
            ComponentType.ReadOnly<BossPresentation>()
        );
        _audioPlayerQuery = _entityManager.CreateEntityQuery(typeof(SoundPlayerTag));

        InitPanels();
    }

    private void Update()
    {
        // CheckPlayerHealth();

        CheckEndRun();
        CheckOpenUpgradesRequest();
        CheckBossAppearance();
    }

    /// <summary>
    /// When the planet's final boss appears (its <see cref="FinalBossTag"/> entity exists), activate the
    /// boss name card and play its reveal once. Re-arms when no final boss is present.
    /// </summary>
    private void CheckBossAppearance()
    {
        if (GameManager.Instance.GetGameState() != EGameState.Running)
            return;

        if (_finalBossQuery.IsEmpty)
        {
            _bossAnnounced = false;
            return;
        }

        if (_bossAnnounced || BossAppearMessage == null)
            return;

        _bossAnnounced = true;

        string bossName = string.Empty;
        _finalBossQuery.CompleteDependency();
        var entities = _finalBossQuery.ToEntityArray(Allocator.Temp);
        if (entities.Length > 0)
        {
            var presentation = _entityManager.GetComponentObject<BossPresentation>(entities[0]);
            if (presentation != null)
                bossName = presentation.DisplayName;
        }
        entities.Dispose();

        if (!_audioPlayerQuery.TryGetSingleton<SoundPlayerTag>(out var soundPlayerTag))
            return;

        soundPlayerTag.HaveBossSpawnedSound = true;
        _audioPlayerQuery.SetSingleton(soundPlayerTag);

        BossAppearMessage.gameObject.SetActive(true);
        BossAppearMessage.Show(bossName);
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
                _entityManager.AddComponentData(
                    reqEntity,
                    new EndRunRequest { State = EEndRunState.Death }
                );

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
            var buffer = _entityManager.GetBuffer<UpgradeSelectionBufferElement>(
                gameStateEntity,
                true
            );

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
        // Sequenced transition: animate the outgoing panel(s) fully out, THEN bring the new one in, so the
        // out- and in-animations never overlap. _transitionGen guards against a newer state change landing
        // mid-transition (a stale "show" is dropped).
        int gen = ++_transitionGen;
        HideActivePanels(() =>
        {
            if (gen == _transitionGen)
                ShowPanelForState(newState);
        });
    }

    // Animates every currently-visible panel out, then invokes onAllHidden once the last one finishes.
    // Panels without an exit animation (upgrade / game over) just switch off and don't hold up the wait.
    private void HideActivePanels(System.Action onAllHidden)
    {
        UpgradeSelectionController.gameObject.SetActive(false);
        GameOverUIController.gameObject.SetActive(false);

        bool hudActive = HUDController.gameObject.activeSelf;
        bool pauseActive = PausePanel.activeSelf;

        int remaining = (hudActive ? 1 : 0) + (pauseActive ? 1 : 0);
        if (remaining == 0)
        {
            onAllHidden?.Invoke();
            return;
        }

        System.Action one = () =>
        {
            if (--remaining == 0)
                onAllHidden?.Invoke();
        };

        if (hudActive)
            HUDController.HideHUD(one);
        if (pauseActive)
            CloseAnimatedPanel(PausePanel, one);
    }

    private void ShowPanelForState(EGameState state)
    {
        switch (state)
        {
            case EGameState.Running:
                HUDController.ShowHUD();
                break;
            case EGameState.Paused:
                PausePanel.SetActive(true);
                break;
            case EGameState.UpgradeSelection:
                UpgradeSelectionController.gameObject.SetActive(true);
                UpgradeSelectionController.ShowSelection();
                break;
            case EGameState.GameOver:
                //GameOverUIController.gameObject.SetActive(true);
                //GameOverUIController.OpenView();
                break;
            default:
                break;
        }
    }

    /// <summary>
    /// Animates a panel out via its <see cref="IUIPanelAnimator"/> (slide or fade), then deactivates it,
    /// or deactivates it immediately when it has no animator. Invokes <paramref name="onComplete"/> once
    /// it is hidden (immediately if already inactive), used to sequence the next panel in.
    /// </summary>
    private static void CloseAnimatedPanel(GameObject panel, System.Action onComplete = null)
    {
        if (panel == null || !panel.activeSelf)
        {
            onComplete?.Invoke();
            return;
        }

        if (panel.TryGetComponent<IUIPanelAnimator>(out var animator))
        {
            animator.Hide(() =>
            {
                panel.SetActive(false);
                onComplete?.Invoke();
            });
        }
        else
        {
            panel.SetActive(false);
            onComplete?.Invoke();
        }
    }
}
