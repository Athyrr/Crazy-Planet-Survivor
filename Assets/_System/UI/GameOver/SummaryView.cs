using Unity.Entities;
using UnityEngine;
using System;
using TMPro;

public class SummaryView : MonoBehaviour
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
        
        float finalHealth = stats.BaseMaxHealth * stats.MaxHealthMultiplier;
        CreateStatUI("Max Health", finalHealth.ToString("0"));
        CreateStatUI("Health Regen", $"{stats.HealthRecovery:0.0}/s");
        CreateStatUI("Armor", stats.BaseArmor.ToString("0"));
        
        float finalSpeed = stats.BaseMoveSpeed * stats.MoveSpeedMultiplier;
        CreateStatUI("Move Speed", finalSpeed.ToString("0.0"));
        
        float finalPickup = stats.BasePickupRange * stats.PickupRangeMultiplier;
        CreateStatUI("Pickup Range", finalPickup.ToString("0.0"));
        
        CreateStatUI("Global Damage", $"{(stats.GlobalDamageMultiplier * 100f):0}%");
        CreateStatUI("Cooldown Time", $"{(stats.GlobalCooldownMultiplier * 100f):0}%");
        CreateStatUI("Area Size", $"{(stats.GlobalSpellAreaMultiplier * 100f):0}%");
        CreateStatUI("Spell Size", $"{(stats.GlobalSpellSizeMultiplier * 100f):0}%");
        CreateStatUI("Projectile Speed", $"{(stats.GlobalSpellSpeedMultiplier * 100f):0}%");
        CreateStatUI("Spell Duration", $"{(stats.GlobalDurationMultiplier * 100f):0}%");

        if (stats.GlobalAmountBonus > 0) 
            CreateStatUI("Bonus Projectiles", $"+{stats.GlobalAmountBonus}");
        
        if (stats.GlobalPierceBonus > 0) 
            CreateStatUI("Bonus Pierces", $"+{stats.GlobalPierceBonus}");
            
        if (stats.GlobalBounceBonus > 0) 
            CreateStatUI("Bonus Bounces", $"+{stats.GlobalBounceBonus}");

        CreateStatUI("Crit Chance", $"{(stats.CritChance * 100f):0.0}%");
        CreateStatUI("Crit Damage", $"x{stats.CritDamageMultiplier:0.0}");
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