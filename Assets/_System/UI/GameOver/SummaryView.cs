using Unity.Entities;
using UnityEngine;
using System;
using System.Reflection;
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
        // Clear previous items
        foreach (Transform child in StatsContainer)
            Destroy(child.gameObject);

        // 1. Display Run Progression Stats
        if (!_runProgressionQuery.IsEmptyIgnoreFilter)
        {
            var run = _runProgressionQuery.GetSingleton<RunProgression>();
            
            CreateStatUI("Total Damage Dealt", $"{(int)run.TotalDamageDealt}");
            CreateStatUI("Total Damage Taken", $"{(int)run.TotalDamageTaken}");
            CreateStatUI("Exp. Collected", $"{(int)run.TotalExperienceCollected}");
        }

        // 2. Display Player Character Stats (Final Values)
        var playerStatsQuery = _entityManager.CreateEntityQuery(typeof(Player), typeof(Stats));
        if (!playerStatsQuery.IsEmptyIgnoreFilter)
        {
            var stats = playerStatsQuery.GetSingleton<Stats>();
            
            // Re-use logic from CharacterStatsViewComponent or implement reflection here
            System.Type type = typeof(BaseStats); // We use BaseStats type for the attributes mapping
            // But we have a Stats instance. We need a way to map Stats fields to BaseStats attributes.
            // Since they are synchronized (same names), we can iterate on BaseStats fields to get the UI attributes
            // then get the value from Stats.

            var fields = type.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
            var statsType = typeof(Stats);

            foreach (var field in fields)
            {
                var attr = field.GetCustomAttribute<UIStatAttribute>();
                if (attr == null) continue;

                // Get value from the runtime Stats component
                var runtimeField = statsType.GetField(field.Name);
                if (runtimeField != null)
                {
                    object rawValue = runtimeField.GetValue(stats);
                    string displayValue = string.Format(attr.Format, rawValue);
                    CreateStatUI(attr.DisplayName, displayValue);
                }
            }
        }
    }

    private void CreateStatUI(string label, string value)
    {
        var uiInstance = Instantiate(SummaryStatPrefab, StatsContainer);
        uiInstance.Init(label, value);
    }
}
