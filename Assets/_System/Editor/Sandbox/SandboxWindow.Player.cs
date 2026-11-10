#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;

public sealed partial class SandboxWindow
{
    private int _playerCharacter = -1;
    private ESpellID _spellToAdd = ESpellID.Fireball;
    private string _upgradeFilter = string.Empty;
    private int _upgradeCount = 1;
    private List<UpgradeConsoleCommands.Entry> _upgrades;
    private string _upgradeError;
    private readonly Dictionary<ESpellID, (int bounce, int pierce)> _counterEdits = new Dictionary<ESpellID, (int, int)>();

    private static string F(float v) => v.ToString(CultureInfo.InvariantCulture);

    partial void DrawPlayerTab()
    {
        if (!_snap.HasPlayer)
        {
            EditorGUILayout.HelpBox("Aucun joueur.", MessageType.Info);
            return;
        }

        // Character
        var characters = SandboxCommandUtils.GetCharacters();
        var labels = new string[characters.Length];
        for (int i = 0; i < characters.Length; i++)
            labels[i] = SandboxCommandUtils.CharacterLabel(i);
        if (_playerCharacter < 0)
            _playerCharacter = Mathf.Max(0, _snap.CharacterIndex);
        using (new EditorGUILayout.HorizontalScope())
        {
            _playerCharacter = EditorGUILayout.Popup($"Perso (actuel : {_snap.Character})", Mathf.Clamp(_playerCharacter, 0, Mathf.Max(0, characters.Length - 1)), labels);
            if (GUILayout.Button("Changer", GUILayout.Width(70)))
                Run($"set_character {_playerCharacter}");
        }

        // Active spells
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Spells actifs", EditorStyles.boldLabel);
        foreach (var s in _snap.Spells)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(s.Id.ToString(), GUILayout.Width(160));
                DrawCapToggle(s, ESpellCapability.Explode, "Explode");
                DrawCapToggle(s, ESpellCapability.Emit, "Emit");
                if (GUILayout.Button("Retirer", GUILayout.Width(60)))
                    Run($"remove_spell {s.Id}");
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (!_counterEdits.TryGetValue(s.Id, out var edit))
                    edit = (s.FinalBounces, s.FinalPierces);
                using (new EditorGUI.DisabledScope((s.Allowed & ESpellCapability.Bounce) == 0))
                {
                    edit.bounce = EditorGUILayout.IntField("Bounce", edit.bounce);
                    if (GUILayout.Button("Fixer", GUILayout.Width(45)))
                        Run($"spell_bounce {s.Id} {edit.bounce}");
                }
                using (new EditorGUI.DisabledScope((s.Allowed & ESpellCapability.Pierce) == 0))
                {
                    edit.pierce = EditorGUILayout.IntField("Pierce", edit.pierce);
                    if (GUILayout.Button("Fixer", GUILayout.Width(45)))
                        Run($"spell_pierce {s.Id} {edit.pierce}");
                }
                _counterEdits[s.Id] = edit;
            }
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            _spellToAdd = (ESpellID)EditorGUILayout.EnumPopup("Ajouter", _spellToAdd);
            if (GUILayout.Button("Ajouter", GUILayout.Width(70)))
                Run($"add_spell {_spellToAdd}");
            if (GUILayout.Button("Vider", GUILayout.Width(60)))
                Run("clear_spells");
        }

        // Upgrades
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Upgrades", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            _upgradeFilter = EditorGUILayout.TextField("Filtre", _upgradeFilter);
            _upgradeCount = Mathf.Max(1, EditorGUILayout.IntField(_upgradeCount, GUILayout.Width(40)));
            if (GUILayout.Button("Recharger", GUILayout.Width(80)) || _upgrades == null)
                _upgradeError = UpgradeConsoleCommands.TryGetUpgradeEntries(out _upgrades, out var err) ? null : err;
        }
        if (_upgradeError != null)
            EditorGUILayout.HelpBox(_upgradeError, MessageType.Warning);
        else if (_upgrades != null)
        {
            int shown = 0;
            foreach (var u in _upgrades)
            {
                if (!string.IsNullOrEmpty(_upgradeFilter) && !u.Matches(_upgradeFilter))
                    continue;
                if (++shown > 40)
                {
                    EditorGUILayout.LabelField("… affine le filtre.");
                    break;
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(u.Label);
                    if (GUILayout.Button("Donner", GUILayout.Width(60)))
                        Run($"give_upgrade {Q(u.AssetName)} {_upgradeCount}");
                }
            }
        }

        // Stats
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Stats (valeurs absolues)", EditorStyles.boldLabel);
        var core = _snap.PlayerStats;
        foreach (var stat in SandboxStatMap.Settable)
        {
            SandboxStatMap.TryGet(core, stat, out var current);
            EditorGUI.BeginChangeCheck();
            float edited = SandboxStatMap.IsInteger(stat)
                ? EditorGUILayout.DelayedIntField(stat.ToString(), Mathf.RoundToInt(current))
                : EditorGUILayout.DelayedFloatField(stat.ToString(), current);
            if (EditorGUI.EndChangeCheck())
                Run($"set_stat {stat} {F(edited)}");
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("reset_stats"))
                Run("reset_stats");
            bool god = _snap.HasSettings && _snap.Settings.PlayerGod;
            bool newGod = GUILayout.Toggle(god, "god");
            if (newGod != god)
                Run($"god {(newGod ? "on" : "off")}");
        }
    }

    private void DrawCapToggle(SandboxMetrics.SpellRow s, ESpellCapability cap, string label)
    {
        using (new EditorGUI.DisabledScope((s.Allowed & cap) == 0))
        {
            bool on = (s.Granted & cap) != 0;
            bool newOn = GUILayout.Toggle(on, label, GUILayout.Width(70));
            if (newOn != on)
                Run($"spell_cap {s.Id} {label} {(newOn ? "on" : "off")}");
        }
    }
}
#endif
