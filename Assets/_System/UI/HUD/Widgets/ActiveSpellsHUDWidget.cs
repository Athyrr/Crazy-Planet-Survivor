using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UI;

/// Displays player's spell currently active during the run.
public class ActiveSpellsHUDWidget : MonoBehaviour
{
    [Header("Databases")] public SpellDatabaseSO SpellsDatabase;
    public AmuletsDatabaseSO AmuletsDatabase;

    [Header("Spells Refs")] public Transform SpellsContainer;
    public ActiveSpellWidgetItem spellWidgetItemPrefab;

    [Header(("Amulet Refs"))] public Transform AmuletContainer;
    public Image AmuletIcon;

    private EntityManager _entityManager;
    private EntityQuery _playerQuery;
    private EntityQuery _gameStateQuery;

    private Dictionary<int, ActiveSpellWidgetItem> _indexToActiveSpellsMap = new();
    private readonly List<int> _staleSpellKeys = new();

    private void Start()
    {
        _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        _playerQuery = _entityManager.CreateEntityQuery(typeof(Player), typeof(ActiveSpell));
        _gameStateQuery = _entityManager.CreateEntityQuery(typeof(GameState));
    }

    private void Update()
    {
        if (_playerQuery.IsEmptyIgnoreFilter)
            return;

        var playerEntity = _playerQuery.GetSingletonEntity();
        var activeSpellsBuffer = _entityManager.GetBuffer<ActiveSpell>(playerEntity);

        var gameStateEntity = _gameStateQuery.GetSingletonEntity();
        var amulet = _entityManager.GetComponentData<EquippedAmulet>(gameStateEntity);

        RefreshSpells(activeSpellsBuffer);
        RefreshAmulet(amulet);
    }

    private void OnDisable()
    {
        Clear();
    }

    private void RefreshAmulet(EquippedAmulet amulet)
    {
        if (!AmuletsDatabase || !AmuletIcon)
            return;
        if (amulet.DbIndex < 0 || amulet.DbIndex >= AmuletsDatabase.Amulets.Length)
            return;

        AmuletIcon.sprite = AmuletsDatabase.Amulets[amulet.DbIndex].Icon;
    }

    private void RefreshSpells(DynamicBuffer<ActiveSpell> activeSpells)
    {
         for (int i = 0; i < activeSpells.Length; i++)
        {
            var activeSpell = activeSpells[i];
            int dbIndex = activeSpell.DatabaseIndex;
            if (dbIndex < 0 || dbIndex >= SpellsDatabase.Spells.Length)
                continue;

            var spellData = SpellsDatabase.Spells[dbIndex];

            var spellWidgetItem = GetOrCreateSpellWidgetItem(dbIndex, spellData, activeSpell);
            if (spellWidgetItem == null)
                continue;

            spellWidgetItem.Refresh(spellData, dbIndex, activeSpell.Level);
            spellWidgetItem.RefreshCooldown(activeSpell.CurrentCooldown, activeSpell.FinalCooldown);
        }

        RemoveStaleSpells(activeSpells);
    }

    /// <summary>Destroys widgets whose spell is no longer in the buffer (e.g. on a new run).</summary>
    private void RemoveStaleSpells(DynamicBuffer<ActiveSpell> activeSpells)
    {
        if (_indexToActiveSpellsMap.Count == activeSpells.Length)
            return;

        _staleSpellKeys.Clear();
        foreach (var key in _indexToActiveSpellsMap.Keys)
        {
            bool present = false;
            for (int i = 0; i < activeSpells.Length; i++)
            {
                if (activeSpells[i].DatabaseIndex == key)
                {
                    present = true;
                    break;
                }
            }

            if (!present)
                _staleSpellKeys.Add(key);
        }

        for (int i = 0; i < _staleSpellKeys.Count; i++)
        {
            int key = _staleSpellKeys[i];
            if (_indexToActiveSpellsMap.TryGetValue(key, out var item) && item != null)
                Destroy(item.gameObject);

            _indexToActiveSpellsMap.Remove(key);
        }
    }

    private ActiveSpellWidgetItem GetOrCreateSpellWidgetItem(int dbIndex, SpellDataSO spellData,
        ActiveSpell activeSpell)
    {
        // Reuse the existing widget unless it was destroyed (Unity-null) — then rebuild it.
        if (_indexToActiveSpellsMap.TryGetValue(dbIndex, out var spellWidgetItem))
        {
            if (spellWidgetItem != null)
                return spellWidgetItem;

            _indexToActiveSpellsMap.Remove(dbIndex);
        }

        if (dbIndex < 0 || dbIndex >= SpellsDatabase.Spells.Length)
            return null;

        //todo  try setactive instead of instanciate (ram vs 0.5ms)
        var newSpellWidgetItem = Instantiate(spellWidgetItemPrefab, SpellsContainer);
        _indexToActiveSpellsMap.Add(dbIndex, newSpellWidgetItem);

        return newSpellWidgetItem;
    }

    private void Clear()
    {
        if (!SpellsContainer)
            return;

        foreach (Transform children in SpellsContainer)
        {
            if (children == null)
                continue;

            Destroy(children.gameObject);
        }

        _indexToActiveSpellsMap.Clear();
    }
}