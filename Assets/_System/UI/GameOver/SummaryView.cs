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

    public void RefreshView()
    {
        RefreshProgressionSummary();
        RefreshSpellsSummary();
        RefreshStatsSummary();
    }

    private void RefreshSpellsSummary()
    {
    }
    private void CreateSpellUI()
    {
    }

    private void RefreshProgressionSummary()
    {
        var entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;

        var runProgressionQuery = entityManager.CreateEntityQuery(typeof(RunProgression));
        var runProgression = runProgressionQuery.GetSingleton<RunProgression>();

        TimeSpan time = TimeSpan.FromSeconds(runProgression.Timer);
        TimeSurvivedText.text = $"{time.Minutes}m {time.Seconds}s";

        EnemiesKilledText.text = $" {runProgression.EnemiesKilledCount}";

        var playerQuery = entityManager.CreateEntityQuery(typeof(PlayerExperience));
        if (!playerQuery.IsEmptyIgnoreFilter)
        {
            var playerExp = playerQuery.GetSingleton<PlayerExperience>();
            LevelText.text = $"{playerExp.Level}";
        }
    }

    private void RefreshStatsSummary()
    {
    }
    private void CreateStatUI()
    {
    }
}
