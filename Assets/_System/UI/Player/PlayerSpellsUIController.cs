using System;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

public class PlayerSpellsUIController : MonoBehaviour
{
    [Header("Database")] public SpellDatabaseSO SpellDatabase;

    [Header("UI References")] public Transform SpellsContainer;
    public UIActiveSpellComponent SpellUIPrefab;

    private EntityManager _entityManager;
    private EntityQuery _playerQuery;

    private Dictionary<int, UIActiveSpellComponent> _indexToActiveSpellsMap =
        new Dictionary<int, UIActiveSpellComponent>();

    private void Start()
    {
        _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        _playerQuery = _entityManager.CreateEntityQuery(typeof(Player), typeof(ActiveSpell));

        Clear();
    }

    private void OnDisable()
    {
        Clear();
    }

    private void Update()
    {
        if (_playerQuery.IsEmptyIgnoreFilter)
            return;

        var playerEntity = _playerQuery.GetSingletonEntity();
        var activeSpellsBuffer = _entityManager.GetBuffer<ActiveSpell>(playerEntity);

        RefreshSpells(activeSpellsBuffer);
    }

    private void RefreshSpells(DynamicBuffer<ActiveSpell> activeSpells)
    {
        for (int i = 0; i < activeSpells.Length; i++)
        {
            var activeSpell = activeSpells[i];
            int dbIndex = activeSpell.DatabaseIndex;
            var spellData = SpellDatabase.Spells[dbIndex];

            if (_indexToActiveSpellsMap.TryGetValue(dbIndex, out var uiComponent)) // if exists -> update
            {
                uiComponent.Refresh(spellData, dbIndex, activeSpell.Level);
            }
            else // else -> create new
            {
                CreateSpellIcon(activeSpell);
            }
        }
    }

    private void CreateSpellIcon(ActiveSpell activeSpell)
    {
        int dbIndex = activeSpell.DatabaseIndex;

        if (dbIndex < 0 || dbIndex >= SpellDatabase.Spells.Length)
            return;

        var spellData = SpellDatabase.Spells[dbIndex];
        var uiActiveSpellComponent = Instantiate(SpellUIPrefab, SpellsContainer);

        uiActiveSpellComponent.Refresh(spellData, dbIndex, activeSpell.Level);

        _indexToActiveSpellsMap.Add(dbIndex, uiActiveSpellComponent);
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