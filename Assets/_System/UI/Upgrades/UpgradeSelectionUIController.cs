using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

public class UpgradeSelectionUIController : UIControllerBase
{
    public UpgradeSelectionView View;

    [Tooltip("Spells database (managed) used to resolve a card's spell tags / level. " +
             "Indexed the same way as ActiveSpell.DatabaseIndex.")]
    public SpellDatabaseSO SpellsDatabase;

    private RunManager _runManager;
    private EntityManager _entityManager;
    private EntityQuery _upgradeDatabaseQuery;
    private EntityQuery _playerQuery;

    private bool _isInitialized = false;

    private readonly List<int> _pendingIndices = new List<int>();

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

    // Stashes the pending selection's database indices. The cards are NOT spawned here: that happens in
    // ShowSelection, which RunManager calls only after the outgoing panel has animated out and this
    // controller's GameObject has been re-activated. Spawning earlier would start the View's entry coroutine
    // while the View's parent is still inactive ("Coroutine couldn't be started ... 'View' is inactive").
    public void DisplaySelection(DynamicBuffer<UpgradeSelectionBufferElement> selection)
    {
        _pendingIndices.Clear();
        for (int i = 0; i < selection.Length; i++)
            _pendingIndices.Add(selection[i].DatabaseIndex);
    }

    // Spawns the cards for the stashed selection. Must run while this controller's GameObject is active so
    // the View (a child) is active-in-hierarchy and can start its entry coroutine.
    public void ShowSelection()
    {
        InitDatabase();

        if (_upgradeDatabaseQuery.IsEmptyIgnoreFilter)
            return;

        var dbEntity = _upgradeDatabaseQuery.GetSingletonEntity();
        var blobs = _entityManager.GetComponentData<UpgradesDatabase>(dbEntity).Blobs;
        ref var upgradesDatabase = ref blobs.Value.Upgrades;

        var context = BuildDisplayContext();
        View.SpawnAndLayoutCards(_pendingIndices, ref upgradesDatabase, in context);
    }

    /// <summary>
    /// Snapshots the live data the cards need but the static upgrade blob lacks: the player's current
    /// stats (stat before → after), a copy of the player's active spells (spell level + spell-stat
    /// before → after) and the spells database (spell id → tags / index).
    /// </summary>
    private UpgradeDisplayContext BuildDisplayContext()
    {
        var context = new UpgradeDisplayContext { SpellsDatabase = SpellsDatabase };

        if (_playerQuery.IsEmptyIgnoreFilter)
            return context;

        var playerEntity = _playerQuery.GetSingletonEntity();

        if (_entityManager.HasComponent<CoreStats>(playerEntity))
        {
            context.PlayerStats = _entityManager.GetComponentData<CoreStats>(playerEntity);
            context.HasPlayerStats = true;
        }

        if (_entityManager.HasBuffer<ActiveSpell>(playerEntity))
        {
            var buffer = _entityManager.GetBuffer<ActiveSpell>(playerEntity, isReadOnly: true);
            var spells = new List<ActiveSpell>(buffer.Length);
            for (int i = 0; i < buffer.Length; i++)
                spells.Add(buffer[i]);
            context.ActiveSpells = spells;
        }

        return context;
    }

    private void HandleUpgradeSelected(int databaseIndex)
    {
        var playerEntity = _playerQuery.GetSingletonEntity();

        _entityManager.AddComponentData(
            playerEntity,
            new ApplyUpgradeRequest { DatabaseIndex = databaseIndex }
        );

        View.ClearSelection();
        View.gameObject.SetActive(false);
        _runManager.TogglePause();
    }
}
