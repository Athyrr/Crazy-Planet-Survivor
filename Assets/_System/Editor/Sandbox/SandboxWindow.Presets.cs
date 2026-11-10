#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEditor;
using UnityEngine;

public sealed partial class SandboxWindow
{
    private string _presetName = string.Empty;

    partial void DrawPresetsSection()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Presets", EditorStyles.boldLabel);
        var names = SandboxPresets.List();
        var options = new string[names.Length + 1];
        options[0] = "(aucun)";
        names.CopyTo(options, 1);
        int current = System.Array.IndexOf(names, _selectedPreset) + 1;
        int picked = EditorGUILayout.Popup("Preset au lancement", current, options);
        _selectedPreset = picked <= 0 ? string.Empty : names[picked - 1];

        using (new EditorGUILayout.HorizontalScope())
        {
            _presetName = EditorGUILayout.TextField("Nom", string.IsNullOrEmpty(_presetName) ? _selectedPreset : _presetName);
            using (new EditorGUI.DisabledScope(!Live || string.IsNullOrWhiteSpace(_presetName)))
                if (GUILayout.Button("Sauver", GUILayout.Width(60)))
                {
                    SandboxPresets.TrySaveCurrent(_presetName, out var msg);
                    _lastOutput = msg;
                }
            using (new EditorGUI.DisabledScope(!Live || string.IsNullOrEmpty(_selectedPreset)))
                if (GUILayout.Button("Charger", GUILayout.Width(65)))
                {
                    if (SandboxPresets.TryLoad(_selectedPreset, out var preset, out var err))
                    {
                        SandboxRunner.Run(SandboxPresets.Apply(preset));
                        _lastOutput = $"Preset '{_selectedPreset}' en cours d'application.";
                    }
                    else
                        _lastOutput = err;
                }
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_selectedPreset)))
                if (GUILayout.Button("Supprimer", GUILayout.Width(75))
                    && EditorUtility.DisplayDialog("Supprimer le preset", $"Supprimer '{_selectedPreset}' ?", "Supprimer", "Annuler"))
                {
                    _lastOutput = SandboxPresets.Delete(_selectedPreset) ? $"Preset '{_selectedPreset}' supprimé." : "Rien à supprimer.";
                    _selectedPreset = string.Empty;
                }
        }
        EditorGUILayout.LabelField("Dossier", SandboxPresets.Folder, EditorStyles.miniLabel);
    }
}
#endif
