using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using _System.Settings; // CPBaseSettings

public class SettingsBrowserWindow : EditorWindow
{
    private enum Tab { Characters, Spells, Global }

    private Tab _tab = Tab.Characters;
    private Vector2 _listScroll;
    private Vector2 _detailScroll;

    private CharacterSO[] _characters;
    private string[] _rosteredCharacterNames;
    private CharacterSO _selectedCharacter;
    private UnityEditor.Editor _selectedCharacterEditor;
    private readonly System.Collections.Generic.List<(FieldInfo field, UnityEditor.Editor editor)> _domainEditors = new();

    private SpellSO[] _spells;
    private string[] _rosteredSpellNames;
    private SpellSO _selectedSpell;
    private UnityEditor.Editor _selectedSpellEditor;

    private Type[] _globalSettingsTypes;
    private UnityEditor.Editor[] _globalSettingsEditors;

    [MenuItem("Survivor/Settings Browser")]
    public static void Open()
    {
        GetWindow<SettingsBrowserWindow>("Settings Browser");
    }

    private void OnEnable()
    {
        RefreshCharacters();
        RefreshSpells();
        RefreshGlobalSettings();
    }

    private void RefreshCharacters()
    {
        _characters = DatabaseAutoPopulateUtils.FindAllAssets<CharacterSO>();

        var db = DatabaseAutoPopulateUtils.FindAllAssets<CharactersDatabaseSO>().FirstOrDefault();
        _rosteredCharacterNames = db != null && db.Characters != null
            ? db.Characters.Where(c => c != null).Select(c => c.name).ToArray()
            : Array.Empty<string>();
    }

    private void RefreshSpells()
    {
        _spells = DatabaseAutoPopulateUtils.FindAllAssets<SpellSO>();

        var db = DatabaseAutoPopulateUtils.FindAllAssets<SpellDatabaseSO>().FirstOrDefault();
        _rosteredSpellNames = db != null && db.Spells != null
            ? db.Spells.Where(s => s != null).Select(s => s.name).ToArray()
            : Array.Empty<string>();
    }

    private void RefreshGlobalSettings()
    {
        _globalSettingsTypes = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
            .Where(t => !t.IsAbstract && typeof(CPBaseSettings).IsAssignableFrom(t) && t != typeof(CPBaseSettings))
            .Where(t => IsCpSettingsSubtype(t))
            .OrderBy(t => t.Name)
            .ToArray();

        _globalSettingsEditors = new UnityEditor.Editor[_globalSettingsTypes.Length];
        for (int i = 0; i < _globalSettingsTypes.Length; i++)
        {
            var instanceProp = _globalSettingsTypes[i].GetProperty("I", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            var instance = instanceProp?.GetValue(null) as ScriptableObject;
            if (instance != null)
                _globalSettingsEditors[i] = UnityEditor.Editor.CreateEditor(instance);
        }
    }

    private static bool IsCpSettingsSubtype(Type t)
    {
        var baseType = t.BaseType;
        while (baseType != null)
        {
            if (baseType.IsGenericType && baseType.GetGenericTypeDefinition().Name.StartsWith("CpSettings"))
                return true;
            baseType = baseType.BaseType;
        }
        return false;
    }

    private void OnGUI()
    {
        DrawTabs();
        EditorGUILayout.Space();

        switch (_tab)
        {
            case Tab.Characters: DrawCharactersTab(); break;
            case Tab.Spells: DrawSpellsTab(); break;
            case Tab.Global: DrawGlobalTab(); break;
        }
    }

    private void DrawTabs()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Toggle(_tab == Tab.Characters, "Characters", EditorStyles.toolbarButton)) _tab = Tab.Characters;
            if (GUILayout.Toggle(_tab == Tab.Spells, "Spells", EditorStyles.toolbarButton)) _tab = Tab.Spells;
            if (GUILayout.Toggle(_tab == Tab.Global, "Global Settings", EditorStyles.toolbarButton)) _tab = Tab.Global;
        }
    }

    private void DrawCharactersTab()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(180)))
            {
                _listScroll = EditorGUILayout.BeginScrollView(_listScroll);
                foreach (var character in _characters)
                {
                    bool inRoster = _rosteredCharacterNames.Contains(character.name);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button(character.name, EditorStyles.label))
                            SelectCharacter(character);
                        if (!inRoster)
                            EditorGUILayout.LabelField("not in roster", EditorStyles.miniLabel, GUILayout.Width(80));
                    }
                }
                EditorGUILayout.EndScrollView();
            }

            using (new EditorGUILayout.VerticalScope())
            {
                _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);
                if (_selectedCharacterEditor != null)
                {
                    EditorGUILayout.LabelField(_selectedCharacter.name, EditorStyles.boldLabel);
                    _selectedCharacterEditor.OnInspectorGUI();

                    foreach (var (field, editor) in _domainEditors)
                    {
                        EditorGUILayout.Space();
                        EditorGUILayout.LabelField(field.Name, EditorStyles.boldLabel);
                        editor.OnInspectorGUI();
                    }
                }
                EditorGUILayout.EndScrollView();
            }
        }
    }

    private void SelectCharacter(CharacterSO character)
    {
        _selectedCharacter = character;
        _selectedCharacterEditor = UnityEditor.Editor.CreateEditor(character);

        _domainEditors.Clear();
        foreach (var field in typeof(CharacterSO).GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!typeof(CharacterSettingsSO).IsAssignableFrom(field.FieldType))
                continue;

            var domainAsset = field.GetValue(character) as CharacterSettingsSO;
            if (domainAsset != null)
                _domainEditors.Add((field, UnityEditor.Editor.CreateEditor(domainAsset)));
        }
    }

    private void DrawSpellsTab()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(220)))
            {
                _listScroll = EditorGUILayout.BeginScrollView(_listScroll);
                foreach (var spell in _spells)
                {
                    bool inRoster = _rosteredSpellNames.Contains(spell.name);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button(spell.name, EditorStyles.label))
                        {
                            _selectedSpell = spell;
                            _selectedSpellEditor = UnityEditor.Editor.CreateEditor(spell);
                        }
                        if (!inRoster)
                            EditorGUILayout.LabelField("not in roster", EditorStyles.miniLabel, GUILayout.Width(80));
                    }
                }
                EditorGUILayout.EndScrollView();
            }

            using (new EditorGUILayout.VerticalScope())
            {
                _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);
                if (_selectedSpellEditor != null)
                {
                    EditorGUILayout.LabelField(_selectedSpell.name, EditorStyles.boldLabel);
                    _selectedSpellEditor.OnInspectorGUI();
                }
                EditorGUILayout.EndScrollView();
            }
        }
    }

    private void DrawGlobalTab()
    {
        _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);
        for (int i = 0; i < _globalSettingsTypes.Length; i++)
        {
            EditorGUILayout.LabelField(_globalSettingsTypes[i].Name, EditorStyles.boldLabel);
            if (_globalSettingsEditors[i] != null)
                _globalSettingsEditors[i].OnInspectorGUI();
            else
                EditorGUILayout.HelpBox("No asset found for this type under Resources/Settings.", MessageType.Warning);
            EditorGUILayout.Space();
        }
        EditorGUILayout.EndScrollView();
    }
}
