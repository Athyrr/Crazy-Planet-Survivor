using Unity.Entities;
using UnityEngine;
using System.Collections.Generic;
using System;

public class GameStatisticsCollector : MonoBehaviour
{
    private EntityManager _entityManager;
    private EntityQuery _statisticsQuery;

    private GameStatisticsService.RunData _currentRunData;
    
    private float _timer;
    private float _sampleRate = 0.5f; // Sample every 0.5 second
    private float _startTime;

    // FPS Calculation
    private float _fpsTimer;
    private int _frameCount;

    private void Start()
    {
        _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        _statisticsQuery = _entityManager.CreateEntityQuery(typeof(GameStatistics));
        
        _startTime = Time.time;
        _currentRunData = new GameStatisticsService.RunData
        {
            SessionID = Guid.NewGuid().ToString(),
            Date = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            Platform = Application.platform.ToString(),
            MinFPS = float.MaxValue,
            MaxFPS = 0
        };
        
        LogEvent("Game Started");
    }

    private void Update()
    {
        // if (!GameplaySettings.I.enableProfiler)
        //     return;

        // FPS Calculation
        _frameCount++;
        _fpsTimer += Time.deltaTime;
        float currentFPS = 0;
        if (_fpsTimer >= 0.5f)
        {
            currentFPS = _frameCount / _fpsTimer;
            _currentRunData.MinFPS = Mathf.Min(_currentRunData.MinFPS, currentFPS);
            _currentRunData.MaxFPS = Mathf.Max(_currentRunData.MaxFPS, currentFPS);
            _frameCount = 0;
            _fpsTimer = 0;
        }

        if (_statisticsQuery.IsEmpty)
            return;

        var stats = _statisticsQuery.GetSingleton<GameStatistics>();
        
        // Update current values
        _currentRunData.EnemiesCreated = stats.EnemiesCreated;
        _currentRunData.EnemiesKilled = stats.EnemiesKilled;
        _currentRunData.UpgradesSelected = stats.UpgradesSelected;
        _currentRunData.PlayerDamageTaken = stats.PlayerDamageTaken;
        _currentRunData.SpellsCasted = stats.SpellsCasted;
        _currentRunData.TotalDamageDealt = stats.TotalDamageDealt;

        // Sample history
        _timer += Time.deltaTime;
        if (_timer >= _sampleRate)
        {
            _timer = 0;
            _currentRunData.EnemiesCreatedHistory.Add(stats.EnemiesCreated);
            _currentRunData.EnemiesKilledHistory.Add(stats.EnemiesKilled);
            _currentRunData.UpgradesSelectedHistory.Add(stats.UpgradesSelected);
            _currentRunData.DamageTakenHistory.Add(stats.PlayerDamageTaken);
            _currentRunData.SpellsCastedHistory.Add(stats.SpellsCasted);
            _currentRunData.TotalDamageDealtHistory.Add(stats.TotalDamageDealt);
            
            if (currentFPS > 0)
                _currentRunData.FPSHistory.Add(currentFPS);
        }
    }

    public void LogEvent(string message)
    {
        float time = Time.time - _startTime;
        _currentRunData.EventLog.Add($"[{time:F2}s] {message}");
    }

    private void OnDestroy()
    {
        if (_currentRunData != null)
        {
            _currentRunData.Duration = Time.time - _startTime;
            
            // Calculate Average FPS
            float totalFPS = 0;
            foreach (var fps in _currentRunData.FPSHistory) totalFPS += fps;
            if (_currentRunData.FPSHistory.Count > 0)
                _currentRunData.AverageFPS = totalFPS / _currentRunData.FPSHistory.Count;

            LogEvent("Game Ended");
            GameStatisticsService.SubmitRunData(_currentRunData);
        }
    }
}
