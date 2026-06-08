using Unity.Entities;
using UnityEngine;
using System;
using TMPro;

public class SummaryListView : UIViewBase
{
    [Header("Databases")] public SpellDatabaseSO SpellsDatabase;

    [Header("Spells")] public Transform SpellsContainer;
    public SummarySpell SummarySpellPrefab;

    [Header("Progression")] public Transform ProgressionContainer;
    public TMP_Text LevelText;
    public TMP_Text TimeSurvivedText;
    public TMP_Text EnemiesKilledText;

    [Header("Stats")] public Transform StatsContainer;
    public SummaryStat SummaryStatPrefab;

    private EntityManager _entityManager;

    private EntityQuery _spellsDatabaseQuery;
    private EntityQuery _playerSpellsQuery;
    private EntityQuery _runProgressionQuery;
    private EntityQuery _playerStatsQuery;

    public void RefreshView()
    {
        _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;

        if (_runProgressionQuery == default)
            _runProgressionQuery = _entityManager.CreateEntityQuery(typeof(RunProgression));

        if (_playerSpellsQuery == default)
            _playerSpellsQuery = _entityManager.CreateEntityQuery(typeof(Player), typeof(ActiveSpell));

        if (_spellsDatabaseQuery == default)
            _spellsDatabaseQuery = _entityManager.CreateEntityQuery(typeof(SpellsDatabase));

        if (_playerStatsQuery == default)
            _playerStatsQuery = _entityManager.CreateEntityQuery(typeof(Player), typeof(CoreStats));

        // Clear views
        foreach (Transform child in SpellsContainer)
            Destroy(child.gameObject);

        foreach (Transform child in StatsContainer)
            Destroy(child.gameObject);

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
        if (_playerStatsQuery.IsEmptyIgnoreFilter)
            return;

        var stats = _playerStatsQuery.GetSingleton<CoreStats>();

        // Absolute totals (health / regen / armor / final move & pickup): flat value, colored by sign.
        float finalHealth = stats.MaxHealth;
        CreateStatUI("Max Health", StatsFormatUtils.Colorize(finalHealth.ToString("0"), finalHealth));
        CreateStatUI("Health Regen", StatsFormatUtils.Colorize($"{stats.HealthRegen:0.0}/s", stats.HealthRegen));
        CreateStatUI("Armor", StatsFormatUtils.Colorize(stats.BaseArmor.ToString("0"), stats.BaseArmor));

        float finalSpeed = stats.BaseMoveSpeed * (1f + stats.MoveSpeed);
        CreateStatUI("Move Speed", StatsFormatUtils.Colorize(finalSpeed.ToString("0.0"), finalSpeed));

        float finalPickup = stats.BasePickupRange * (1f + stats.PickupRange);
        CreateStatUI("Pickup Range", StatsFormatUtils.Colorize(finalPickup.ToString("0.0"), finalPickup));

        // Global multiplier bonuses: signed percentage (0% stays green and keeps the %).
        CreateStatUI("Damage", StatsFormatUtils.FormatValue(stats.Damage, isPercentage: true));
        CreateStatUI("Attack Sp.", StatsFormatUtils.FormatValue(stats.AttackSpeed, isPercentage: true));
        CreateStatUI("Spell Size", StatsFormatUtils.FormatValue(stats.SpellSize, isPercentage: true));
        CreateStatUI("Spell Sp.", StatsFormatUtils.FormatValue(stats.SpellSpeed, isPercentage: true));
        CreateStatUI("Spell Duration", StatsFormatUtils.FormatValue(stats.SpellDuration, isPercentage: true));

        // Flat count bonuses: only shown when present.
        if (stats.Amount > 0)
            CreateStatUI("Amount", StatsFormatUtils.FormatValue(stats.Amount, isPercentage: false));

        if (stats.Pierce > 0)
            CreateStatUI("Pierce", StatsFormatUtils.FormatValue(stats.Pierce, isPercentage: false));

        if (stats.Bounce > 0)
            CreateStatUI("Bounce", StatsFormatUtils.FormatValue(stats.Bounce, isPercentage: false));

        CreateStatUI("Crit Chance", StatsFormatUtils.FormatValue(stats.CritChance, isPercentage: true));
        CreateStatUI("Crit Damage", StatsFormatUtils.FormatValue(stats.CritDamage, isPercentage: true));
    }

    private void CreateSpellUI(ref SpellBlob spellData, ActiveSpell activeSpell)
    {
        var icon = SpellsDatabase.Spells[activeSpell.DatabaseIndex].Icon;
        var uiInstance = Instantiate(SummarySpellPrefab, SpellsContainer);
        uiInstance.Refresh(spellData, activeSpell, icon);
    }

    private void CreateStatUI(string label, string value)
    {
        var uiInstance = Instantiate(SummaryStatPrefab, StatsContainer);
        uiInstance.Refresh(label, value);
    }
}