#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Globalization;
using UnityEditor;
using UnityEngine;

public sealed partial class SandboxWindow
{
    private int _maxEnemiesEdit = -1;
    private float _difficultyEdit;
    private float _immortalDelayEdit = SandboxSettings.DefaultRegenDelay;
    private int _enemyType;
    private int _enemyCount = 1;
    private int _dummyCount = 1;
    private int _dummyHp = 10000;
    private float _dummyArmor;

    partial void DrawEnemiesTab()
    {
        var s = _snap.HasSettings ? _snap.Settings : SandboxSettings.Off;

        EditorGUILayout.LabelField("Ennemis actifs", _snap.ActiveEnemyCount.ToString());
        Toggle("Spawn", !s.SpawnPaused, on => $"spawn {(on ? "on" : "off")}");

        using (new EditorGUI.DisabledScope(_snap.MaxEnemies < 0))
        using (new EditorGUILayout.HorizontalScope())
        {
            if (_maxEnemiesEdit < 0)
                _maxEnemiesEdit = Mathf.Max(0, _snap.MaxEnemies);
            _maxEnemiesEdit = EditorGUILayout.IntField($"Max (actuel {_snap.MaxEnemies})", _maxEnemiesEdit);
            if (GUILayout.Button("Fixer", GUILayout.Width(50)))
                Run($"max_enemies {_maxEnemiesEdit}");
        }
        using (new EditorGUI.DisabledScope(!_snap.HasDirector))
            Toggle("Director", _snap.DirectorEnabled, on => $"director {(on ? "on" : "off")}");

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("Difficulté", s.DifficultyLevel < 0f ? "off" : s.DifficultyLevel.ToString("0.##"));
            _difficultyEdit = Mathf.Max(0f, EditorGUILayout.FloatField(_difficultyEdit, GUILayout.Width(50)));
            if (GUILayout.Button("Forcer", GUILayout.Width(55)))
                Run($"difficulty {_difficultyEdit.ToString(CultureInfo.InvariantCulture)}");
            if (GUILayout.Button("off", GUILayout.Width(35)))
                Run("difficulty off");
        }

        EditorGUILayout.Space();
        Toggle("Figés (enemies_freeze)", s.EnemiesFrozen, on => $"enemies_freeze {(on ? "on" : "off")}");
        Toggle("Invincibles", s.EnemiesInvincible, on => $"enemies_invincible {(on ? "on" : "off")}");
        using (new EditorGUILayout.HorizontalScope())
        {
            _immortalDelayEdit = Mathf.Max(0f, EditorGUILayout.FloatField("Délai immortel (s)", _immortalDelayEdit));
            Toggle("Immortels", s.EnemiesImmortal,
                on => $"enemies_immortal {(on ? "on" : "off")} {_immortalDelayEdit.ToString(CultureInfo.InvariantCulture)}");
        }
        if (GUILayout.Button("kill_all"))
            Run("kill_all");

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Sandbox", EditorStyles.boldLabel);
        if (!_snap.IsSandbox)
        {
            EditorGUILayout.HelpBox("Spawn d'un type et dummies : Sandbox uniquement.", MessageType.Info);
            return;
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            var types = _snap.EnemyTypes.ToArray();
            _enemyType = types.Length == 0 ? 0 : EditorGUILayout.Popup("Type", Mathf.Clamp(_enemyType, 0, types.Length - 1), types);
            _enemyCount = Mathf.Max(1, EditorGUILayout.IntField(_enemyCount, GUILayout.Width(40)));
            using (new EditorGUI.DisabledScope(types.Length == 0))
                if (GUILayout.Button("spawn_enemy", GUILayout.Width(95)))
                    Run($"spawn_enemy {Q(types[_enemyType])} {_enemyCount}");
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            _dummyCount = Mathf.Max(1, EditorGUILayout.IntField("Dummies", _dummyCount));
            if (GUILayout.Button("spawn_dummy", GUILayout.Width(95)))
                Run($"spawn_dummy {_dummyCount}");
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            _dummyHp = Mathf.Max(1, EditorGUILayout.IntField("PV du dummy", _dummyHp));
            if (GUILayout.Button("Fixer", GUILayout.Width(50)))
                Run($"dummy_hp {_dummyHp}");
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            _dummyArmor = Mathf.Max(0f, EditorGUILayout.FloatField("Armure du dummy", _dummyArmor));
            if (GUILayout.Button("Fixer", GUILayout.Width(50)))
                Run($"dummy_armor {_dummyArmor.ToString(CultureInfo.InvariantCulture)}");
        }
    }

    private void Toggle(string label, bool current, System.Func<bool, string> command)
    {
        bool next = EditorGUILayout.Toggle(label, current);
        if (next != current)
            Run(command(next));
    }
}
#endif
