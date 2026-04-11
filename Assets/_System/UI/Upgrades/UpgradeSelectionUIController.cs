using System.Collections.Generic;
using Unity.Entities;

public class UpgradeSelectionUIController : UIControllerBase
{
    public UpgradeSelectionView View;

    private RunManager _runManager;
    private EntityManager _entityManager;
    private EntityQuery _upgradeDatabaseQuery;
    private EntityQuery _playerQuery;

    private bool _isInitialized = false;

    private void Awake()
    {
        InitDatabase();
    }

    private void OnEnable() => View.OnUpgradeSelected += HandleUpgradeSelected;

    private void OnDisable() => View.OnUpgradeSelected -= HandleUpgradeSelected;

    public void Init(RunManager runManager)
    {
        _runManager = runManager;
        InitDatabase();
    }

    private void InitDatabase()
    {
        if (_isInitialized) 
            return;
        
        _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;

        _upgradeDatabaseQuery = _entityManager.CreateEntityQuery(typeof(UpgradesDatabase));
        _playerQuery = _entityManager.CreateEntityQuery(typeof(Player));

        _isInitialized = true;
    }
    
    public void DisplaySelection(DynamicBuffer<UpgradeSelectionBufferElement> selection)
    {
        InitDatabase();

        if (_upgradeDatabaseQuery.IsEmptyIgnoreFilter)
            return;

        var dbEntity = _upgradeDatabaseQuery.GetSingletonEntity();
        var blobs = _entityManager.GetComponentData<UpgradesDatabase>(dbEntity).Blobs;
        ref var upgradesDatabase = ref blobs.Value.Upgrades;

        List<int> indices = new List<int>();
        for (int i = 0; i < selection.Length; i++)
        {
            indices.Add(selection[i].DatabaseIndex);
        }

        View.SpawnAndLayoutCards(indices, ref upgradesDatabase);
    }

    private void HandleUpgradeSelected(int databaseIndex)
    {
        var playerEntity = _playerQuery.GetSingletonEntity();

        _entityManager.AddComponentData(playerEntity, new ApplyUpgradeRequest { DatabaseIndex = databaseIndex });

        View.ClearSelection();
        View.gameObject.SetActive(false);
        _runManager.TogglePause();
    }
}