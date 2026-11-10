#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEditor;
using UnityEngine;

/// <summary>"Sandbox" window: an interface over the sandbox console commands. Reads ECS only through
/// SandboxMetrics.Capture at ~4 Hz; empty outside Play. Player / Enemies tabs and the presets section live in their own
/// partial files.</summary>
public sealed partial class SandboxWindow : EditorWindow
{
    private static readonly string[] TabNames = { "Lancer", "Joueur", "Ennemis", "Mesures" };
    private const double RefreshPeriod = 0.25;

    private int _tab;
    private double _nextRefresh;
    private Vector2 _scroll;
    private string _lastOutput = string.Empty;
    private SandboxMetrics.Snapshot _snap;
    private readonly SandboxDpsTracker _dps = new SandboxDpsTracker();

    private int _launchCharacter;
    private string _selectedPreset = string.Empty;
    private float _measureSeconds = 10f;
    private bool _measureFreeze = true;
    private string _ttkType = "Snail";
    private int _ttkCount = 10;

    [MenuItem("Survivor/Sandbox")]
    private static void Open() => GetWindow<SandboxWindow>("Sandbox");

    private void OnEnable()
    {
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    private void OnDisable()
    {
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
    }

    private bool Live => EditorApplication.isPlaying && EditorApplication.isPlayingOrWillChangePlaymode && _snap != null && _snap.Valid;

    private void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.ExitingPlayMode || change == PlayModeStateChange.EnteredEditMode)
        {
            _snap = null;
            _dps.Clear();
            Repaint();
        }
    }

    private void Tick()
    {
        if (!EditorApplication.isPlaying || !EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        if (EditorApplication.timeSinceStartup < _nextRefresh)
            return;
        _nextRefresh = EditorApplication.timeSinceStartup + RefreshPeriod;

        _snap = SandboxMetrics.Capture();
        if (_snap.Valid)
            foreach (var d in _snap.Dummies)
                _dps.Add(d.Entity, _snap.Time, d.Total);
        Repaint();
    }

    private void OnGUI()
    {
        _tab = GUILayout.Toolbar(_tab, TabNames);
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        if (_tab == 0)
            DrawLaunchTab();
        else if (!Live)
            EditorGUILayout.HelpBox("Disponible en Play (une run ou la Sandbox chargée).", MessageType.Info);
        else if (_tab == 1)
            DrawPlayerTab();
        else if (_tab == 2)
            DrawEnemiesTab();
        else
            DrawMeasuresTab();

        EditorGUILayout.EndScrollView();
        if (!string.IsNullOrEmpty(_lastOutput))
            EditorGUILayout.HelpBox(_lastOutput, MessageType.None);
    }

    partial void DrawPlayerTab();
    partial void DrawEnemiesTab();
    partial void DrawPresetsSection();

    private void Run(string commandLine)
    {
        var result = ConsoleCommandInvoker.Execute(commandLine);
        _lastOutput = $"> {commandLine}\n{result.Output}";
        DeveloperConsole.Print(_lastOutput);
        _nextRefresh = 0; // refresh on next tick
    }

    private static string Q(string s) => "\"" + s + "\"";

    private void DrawLaunchTab()
    {
        var characters = SandboxCommandUtils.GetCharacters();
        var labels = new string[characters.Length];
        for (int i = 0; i < characters.Length; i++)
            labels[i] = SandboxCommandUtils.CharacterLabel(i);
        if (characters.Length > 0)
            _launchCharacter = EditorGUILayout.Popup("Perso", Mathf.Clamp(_launchCharacter, 0, characters.Length - 1), labels);
        else
            EditorGUILayout.HelpBox("Base de persos introuvable.", MessageType.Warning);

        DrawPresetsSection();

        using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying || characters.Length == 0))
        {
            if (GUILayout.Button("Lancer la sandbox", GUILayout.Height(28)))
            {
                string line = $"sandbox {_launchCharacter}";
                if (!string.IsNullOrEmpty(_selectedPreset))
                    line += " " + Q(_selectedPreset);
                Run(line);
            }
        }
        if (!EditorApplication.isPlaying)
            EditorGUILayout.HelpBox("Entre en Play dans SC_Main, puis lance la sandbox.", MessageType.Info);
    }

    private void DrawMeasuresTab()
    {
        EditorGUILayout.LabelField("Morts du joueur", _snap.HasSettings ? _snap.Settings.PlayerDeaths.ToString() : "0");

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Dummies (après armure)", EditorStyles.boldLabel);
        if (_snap.Dummies.Count == 0)
            EditorGUILayout.LabelField("Aucun dummy (Sandbox uniquement).");
        foreach (var d in _snap.Dummies)
            EditorGUILayout.LabelField($"Dummy {d.Entity.Index}",
                $"total {d.Total:0}   DPS 5 s {_dps.Dps(d.Entity):0.0}   plus gros coup {d.MaxHit:0}   PV {d.Health}/{d.MaxHealth:0}   armure {d.Armor:0.#}");

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Spells (avant armure)", EditorStyles.boldLabel);
        double sinceReset = System.Math.Max(1e-6, _snap.Time - SandboxSession.MeasureResetTime);
        foreach (var s in _snap.Spells)
            EditorGUILayout.LabelField(s.Id.ToString(), $"total {s.TotalDamage:0}   DPS depuis reset {s.TotalDamage / sinceReset:0.0}");

        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("dps_reset"))
            {
                Run("dps_reset");
                _dps.Clear();
            }
            _measureSeconds = EditorGUILayout.FloatField(_measureSeconds, GUILayout.Width(50));
            _measureFreeze = GUILayout.Toggle(_measureFreeze, "freeze", GUILayout.Width(60));
            if (GUILayout.Button("measure"))
                Run($"measure {_measureSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}{(_measureFreeze ? " freeze" : "")}");
        }
        using (new EditorGUI.DisabledScope(!_snap.IsSandbox))
        using (new EditorGUILayout.HorizontalScope())
        {
            _ttkType = EditorGUILayout.TextField(_ttkType);
            _ttkCount = EditorGUILayout.IntField(_ttkCount, GUILayout.Width(50));
            if (GUILayout.Button("ttk"))
                Run($"ttk {Q(_ttkType)} {_ttkCount}");
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Dernière mesure", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(string.IsNullOrEmpty(SandboxSession.LastMeasureResult) ? "—" : SandboxSession.LastMeasureResult, MessageType.None);
        EditorGUILayout.LabelField("Dernier ttk", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(string.IsNullOrEmpty(SandboxSession.LastTtkResult) ? "—" : SandboxSession.LastTtkResult, MessageType.None);
    }
}
#endif
