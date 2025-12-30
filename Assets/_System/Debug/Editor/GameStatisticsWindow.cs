#if ENABLE_STATISTICS
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

public class GameStatisticsWindow : EditorWindow
{
    private Vector2 _scrollPosition;
    private bool _showLogs = false;

    [MenuItem("Tools/Game Statistics/Game Statistics Window", priority = 0)]
    public static void ShowWindow()
    {
        GetWindow<GameStatisticsWindow>("Game Statistics");
    }

    private void OnGUI()
    {
        var data = GameStatisticsService.LastRunData;

        if (data == null)
        {
            GUILayout.Label("No data available from the last run.", EditorStyles.boldLabel);
            return;
        }

        _scrollPosition = GUILayout.BeginScrollView(_scrollPosition);

        // Header Section
        GUILayout.BeginVertical("box");
        GUILayout.Label($"Session ID: {data.SessionID}", EditorStyles.miniLabel);
        GUILayout.Label($"Date: {data.Date} | Duration: {data.Duration:F2}s | Platform: {data.Platform}", EditorStyles.miniLabel);
        GUILayout.EndVertical();

        GUILayout.Space(10);
        GUILayout.Label("Performance", EditorStyles.boldLabel);
        GUILayout.BeginHorizontal();
        GUILayout.Label($"Avg FPS: {data.AverageFPS:F1}");
        GUILayout.Label($"Min FPS: {data.MinFPS:F1}");
        GUILayout.Label($"Max FPS: {data.MaxFPS:F1}");
        GUILayout.EndHorizontal();

        GUILayout.Space(10);
        GUILayout.Label("Gameplay Statistics", EditorStyles.boldLabel);
        
        GUILayout.BeginHorizontal();
        GUILayout.BeginVertical("box");
        GUILayout.Label($"Enemies Created: {data.EnemiesCreated}");
        GUILayout.Label($"Enemies Killed: {data.EnemiesKilled}");
        GUILayout.EndVertical();
        
        GUILayout.BeginVertical("box");
        GUILayout.Label($"Upgrades Selected: {data.UpgradesSelected}");
        GUILayout.Label($"Spells Casted: {data.SpellsCasted}");
        GUILayout.EndVertical();
        
        GUILayout.BeginVertical("box");
        GUILayout.Label($"Player Damage Taken: {data.PlayerDamageTaken}");
        GUILayout.Label($"Total Damage Dealt: {data.TotalDamageDealt:F0}");
        GUILayout.EndVertical();
        GUILayout.EndHorizontal();

        GUILayout.Space(20);
        GUILayout.Label("Graphs", EditorStyles.boldLabel);

        DrawGraph("Enemies Created", data.EnemiesCreatedHistory, Color.red);
        DrawGraph("Enemies Killed", data.EnemiesKilledHistory, Color.green);
        DrawGraph("Upgrades Selected", data.UpgradesSelectedHistory, Color.blue);
        DrawGraph("Damage Taken", data.DamageTakenHistory, Color.magenta);
        DrawGraph("Spells Casted", data.SpellsCastedHistory, Color.cyan);
        DrawGraph("Total Damage Dealt", data.TotalDamageDealtHistory.Select(x => (int)x).ToList(), Color.yellow);
        DrawGraph("FPS", data.FPSHistory.Select(x => (int)x).ToList(), Color.white);

        GUILayout.Space(20);
        _showLogs = EditorGUILayout.Foldout(_showLogs, "Event Logs");
        if (_showLogs)
        {
            GUILayout.BeginVertical("box");
            foreach (var log in data.EventLog)
            {
                GUILayout.Label(log, EditorStyles.miniLabel);
            }
            GUILayout.EndVertical();
        }

        GUILayout.EndScrollView();
    }

    private void DrawGraph(string title, List<int> history, Color color)
    {
        GUILayout.Label(title);
        Rect rect = GUILayoutUtility.GetRect(position.width - 40, 100); // Adjusted width for scrollbar
        EditorGUI.DrawRect(rect, new Color(0.1f, 0.1f, 0.1f));

        if (history == null || history.Count < 2) return;

        float max = history.Max();
        if (max == 0) max = 1;

        float width = rect.width / (history.Count - 1);
        
        Handles.color = color;
        Vector3[] points = new Vector3[history.Count];

        for (int i = 0; i < history.Count; i++)
        {
            float height = (history[i] / max) * rect.height;
            points[i] = new Vector3(rect.x + i * width, rect.y + rect.height - height, 0);
        }

        Handles.DrawAAPolyLine(2f, points);
        GUILayout.Space(10);
    }
}
#else
public class GameStatisticsWindow : EditorWindow
{
    private Vector2 _scrollPosition;
    private bool _showLogs = false;

    [MenuItem("Tools/Game Statistics/Game Statistics Window", priority = 0)]
    public static void ShowWindow()
    {
        GetWindow<GameStatisticsWindow>("Game Statistics");
    }

    private void OnGUI()
    {
        GUILayout.Label("Statistics Disabled, enable the toggle in the tool menu", EditorStyles.boldLabel);
    }
}
#endif
