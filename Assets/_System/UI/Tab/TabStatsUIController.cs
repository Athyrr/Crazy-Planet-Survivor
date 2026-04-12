using Unity.Entities;
using UnityEngine;

public class TabStatsUIController : UIControllerBase
{
    [Header("View Reference")] public TabStatsUIView TabStatsView;

    private EntityManager _entityManager;
    private EntityQuery _inputsEntityQuery;
    private EntityQuery _coreStatsQuery;

    private void Awake()
    {
        _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
    }

    private void Start()
    {
        _inputsEntityQuery = _entityManager.CreateEntityQuery(typeof(InputData));
        _coreStatsQuery = _entityManager.CreateEntityQuery(typeof(CoreStats), typeof(Player));
    }

    private void Update()
    {
        if (_inputsEntityQuery.IsEmpty)
            return;

        InputData input = _inputsEntityQuery.GetSingleton<InputData>();

        if (input.IsTabPressed && !TabStatsView.IsOpen)
        {
            TabStatsView.gameObject.SetActive(true);
            TabStatsView.OpenView();

            if (!_coreStatsQuery.IsEmpty)
            {
                TabStatsView.RefreshData(_coreStatsQuery.GetSingleton<CoreStats>());
            }
        }
        else if (input.IsTabPressed && TabStatsView.IsOpen)
        {
            TabStatsView.CloseView();
        }
    }
}