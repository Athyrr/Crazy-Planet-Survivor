using UnityEngine;
using System.Collections.Generic;
using System;

public static class GameStatisticsService
{
    [Serializable]
    public class RunData
    {
        // Session Metadata
        public string SessionID;
        public string Date;
        public float Duration;
        public string Platform;

        // Gameplay Stats
        public int EnemiesCreated;
        public int EnemiesKilled;
        public int UpgradesSelected;
        public int PlayerDamageTaken;
        public int SpellsCasted;
        public float TotalDamageDealt;
        
        // Performance Stats
        public float AverageFPS;
        public float MinFPS;
        public float MaxFPS;

        // Histories
        public List<int> EnemiesCreatedHistory = new List<int>();
        public List<int> EnemiesKilledHistory = new List<int>();
        public List<int> UpgradesSelectedHistory = new List<int>();
        public List<int> DamageTakenHistory = new List<int>();
        public List<int> SpellsCastedHistory = new List<int>();
        public List<float> TotalDamageDealtHistory = new List<float>();
        public List<float> FPSHistory = new List<float>();

        // Event Log (Timestamp, Message)
        public List<string> EventLog = new List<string>();
    }

    public static RunData LastRunData { get; private set; }

    public static void SubmitRunData(RunData data)
    {
        LastRunData = data;
        // @todo: Send data to external API
        Debug.Log($"Game Statistics submitted for session {data.SessionID}.");
    }
}
