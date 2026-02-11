using Unity.Entities;
using UnityEngine;
using System;
using TMPro;

public class SummaryView : MonoBehaviour
{
    [Header("Spells")]
    public Transform SpellsContainer;
    public SummarySpell SummarySpellPrefab;

    [Header("Progression")]
    public Transform ProgressionContainer;
    public TMP_Text LevelText;
    public TMP_Text TimeSurvivedText;
    public TMP_Text EnemiesKilledText;

    [Header("Stats")]
    public Transform StatsContainer;
    public SummaryStat SummaryStatPrefab;

    private EntityManager _entityManager;

    private EntityQuery _spellsDatabaseQuery;
    private EntityQuery _playerSpellsQuery;
    private EntityQuery _runProgressionQuery;

    public void RefreshView()
    {
        _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;

        if (_runProgressionQuery == default)
            _runProgressionQuery = _entityManager.CreateEntityQuery(typeof(RunProgression));

        if (_playerSpellsQuery == default)
            _playerSpellsQuery = _entityManager.CreateEntityQuery(typeof(Player), typeof(ActiveSpell));
       
        if (_spellsDatabaseQuery == default)
            _spellsDatabaseQuery = _entityManager.CreateEntityQuery(typeof(SpellsDatabase));

        // Clear views
        foreach (Transform child in SpellsContainer)
            Destroy(child.gameObject);

        //foreach (Transform child in StatsContainer)
        //    Destroy(child.gameObject);

        RefreshProgressionSummary();
        RefreshSpellsSummary();
        RefreshStatsSummary();
    }

    private void RefreshSpellsSummary()
    {
        if (_spellsDatabaseQuery.IsEmptyIgnoreFilter)
            return;

        var databaseEntity = _spellsDatabaseQuery.GetSingletonEntity();
        var databaseRef = _entityManager.GetComponentData<SpellsDatabase>(databaseEntity).Blobs;

        ref var spellsDatabase = ref databaseRef.Value.Spells;

        if (_playerSpellsQuery.IsEmptyIgnoreFilter)
            return;

        var playerSpells = _playerSpellsQuery.GetSingletonBuffer<ActiveSpell>();

        foreach (var activeSpell in playerSpells)
        {
            int index = activeSpell.DatabaseIndex;
            if (index < 0 || index >= spellsDatabase.Length)
                continue;

            ref var spellData = ref spellsDatabase[index];
            CreateSpellUI(ref spellData, activeSpell);
        }
    }

    private void RefreshProgressionSummary()
    {
        var runProgression = _runProgressionQuery.GetSingleton<RunProgression>();

        TimeSpan time = TimeSpan.FromSeconds(runProgression.Timer);
        TimeSurvivedText.text = $"{time.Minutes}m {time.Seconds}s";

        EnemiesKilledText.text = $" {runProgression.EnemiesKilledCount}";

        var playerQuery = _entityManager.CreateEntityQuery(typeof(PlayerExperience));
        if (!playerQuery.IsEmptyIgnoreFilter)
        {
            var playerExp = playerQuery.GetSingleton<PlayerExperience>();
            LevelText.text = $"{playerExp.Level}";
        }
    }

    private void RefreshStatsSummary()
    {
    }

    private void CreateSpellUI(ref SpellBlob spellData, ActiveSpell activeSpell)
    {
        var uiInstance = Instantiate(SummarySpellPrefab, SpellsContainer);
        uiInstance.Refresh(spellData, activeSpell);
    }

    private void CreateStatUI()
    {
    }
}
