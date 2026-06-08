using Unity.Entities;
using UnityEngine;
using UnityEngine.InputSystem;

public class TabStatsUIController : UIControllerBase
{
    [Header("View Reference")] public TabStatsUIView TabStatsView;

    private EntityManager _entityManager;
    private EntityQuery _coreStatsQuery;

    private GameInputs _inputs;

    private void Awake()
    {
        _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        _coreStatsQuery = _entityManager.CreateEntityQuery(typeof(CoreStats), typeof(Player));
    }

    private void OnEnable()
    {
        if (_inputs == null)
            _inputs = new GameInputs();

        _inputs.Player.StatsView.performed += OnToggleStats;
        _inputs.Player.StatsView.Enable();

        RefreshView();
    }

    private void OnDisable()
    {
        if (_inputs == null)
            return;

        _inputs.Player.StatsView.performed -= OnToggleStats;
        _inputs.Player.StatsView.Disable();
    }

    private void OnToggleStats(InputAction.CallbackContext ctx)
    {
        if (!TabStatsView.IsOpen)
        {
            TabStatsView.gameObject.SetActive(true);
            TabStatsView.OpenView();
            RefreshView();
        }
        else
        {
            TabStatsView.CloseView();
        }
    }

    private void RefreshView()
    {
        if (!_coreStatsQuery.IsEmpty)
        {
            TabStatsView.RefreshData(_coreStatsQuery.GetSingleton<CoreStats>());
        }
    }
}
