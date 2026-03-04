using System;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UI;

public class PlayerSpellsUIController : MonoBehaviour
{
    [Header("Databases")] public SpellDatabaseSO SpellsDatabase;
    public AmuletsDatabaseSO AmuletsDatabase;


    [Header("UI References")] public Transform SpellsContainer;
    public UIActiveSpellComponent SpellUIPrefab;

    public Transform AmuletContainer;
    public Image AmuletIcon;

    private EntityManager _entityManager;
    private EntityQuery _playerQuery;
    private EntityQuery _gameStateQuery;

    private Dictionary<int, UIActiveSpellComponent> _indexToActiveSpellsMap =
        new Dictionary<int, UIActiveSpellComponent>();

    private void Start()
    {
        _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        _playerQuery = _entityManager.CreateEntityQuery(typeof(Player), typeof(ActiveSpell));
        _gameStateQuery = _entityManager.CreateEntityQuery(typeof(GameState));

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

        var gameStateEntity = _gameStateQuery.GetSingletonEntity();
        var amulet = _entityManager.GetComponentData<EquippedAmulet>(gameStateEntity);

        RefreshSpells(activeSpellsBuffer);
        RefreshAmulet(amulet);
    }

    private void RefreshAmulet(EquippedAmulet amulet)
    {
        if (!AmuletsDatabase || !AmuletContainer)
            return;

        if (amulet.DbIndex < 0 || amulet.DbIndex >= AmuletsDatabase.Amulets.Length)
            return;

        var amuletData = AmuletsDatabase.Amulets[amulet.DbIndex];

        if (AmuletIcon)
            AmuletIcon.sprite = amuletData.Icon;
    }

    private void RefreshSpells(DynamicBuffer<ActiveSpell> activeSpells)
    {
        for (int i = 0; i < activeSpells.Length; i++)
        {
            var activeSpell = activeSpells[i];
            int dbIndex = activeSpell.DatabaseIndex;
            var spellData = SpellsDatabase.Spells[dbIndex];

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

        if (dbIndex < 0 || dbIndex >= SpellsDatabase.Spells.Length)
            return;

        var spellData = SpellsDatabase.Spells[dbIndex];
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