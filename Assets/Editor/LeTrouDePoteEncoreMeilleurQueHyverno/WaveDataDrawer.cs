using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Custom drawer for <see cref="EnemiesSpawnerAuthoring.WaveData"/>. On top of the normal fields it shows
/// per-wave feedback in the inspector:
///  - auto-detects which wave contains the final boss (a group whose prefab has a <see cref="BossAuthoring"/>
///    configured as FinalBoss) and labels it,
///  - flags an error if more than one wave contains a final boss (the design is always exactly one),
///  - flags an error if a wave has its AroundBoss flag set but is not placed AFTER the boss wave (or there
///    is no boss wave at all).
/// Detection mirrors the baker's ContainsFinalBoss logic so the editor and runtime agree.
/// </summary>
[CustomPropertyDrawer(typeof(EnemiesSpawnerAuthoring.WaveData))]
public class WaveDataDrawer : PropertyDrawer
{
    private const float Spacing = 2f;
    private static float LineH => EditorGUIUtility.singleLineHeight;
    private static float MsgH => EditorGUIUtility.singleLineHeight * 3.2f; // fixed so OnGUI / height agree; generous so long messages don't clip

    private struct Note
    {
        public string Text;
        public MessageType Type;
    }

    // ---- detection helpers (mirror the baker) ----

    private static bool WaveHasFinalBoss(EnemiesSpawnerAuthoring.WaveData wave)
    {
        if (wave.Groups == null) return false;
        foreach (var g in wave.Groups)
        {
            if (g.Prefab == null) continue;
            var ba = g.Prefab.GetComponent<BossAuthoring>();
            if (ba != null && (ba.Config == null || ba.Config.Kind == EBossKind.FinalBoss))
                return true;
        }
        return false;
    }

    private static bool WaveHasAroundBoss(EnemiesSpawnerAuthoring.WaveData wave)
    {
        return wave.AroundBoss;
    }

    private static int IndexFromPath(string path)
    {
        int open = path.LastIndexOf('[');
        int close = path.LastIndexOf(']');
        if (open >= 0 && close > open && int.TryParse(path.Substring(open + 1, close - open - 1), out int idx))
            return idx;
        return -1;
    }

    // ---- per-wave messages ----

    private static List<Note> BuildNotes(SerializedProperty property)
    {
        var notes = new List<Note>();
        var authoring = property.serializedObject.targetObject as EnemiesSpawnerAuthoring;
        if (authoring == null || authoring.Waves == null) return notes;

        int index = IndexFromPath(property.propertyPath);
        if (index < 0 || index >= authoring.Waves.Length) return notes;

        // First boss wave (and whether there are several).
        int firstBoss = -1;
        int bossCount = 0;
        for (int i = 0; i < authoring.Waves.Length; i++)
        {
            if (WaveHasFinalBoss(authoring.Waves[i]))
            {
                bossCount++;
                if (firstBoss < 0) firstBoss = i;
            }
        }

        var wave = authoring.Waves[index];
        bool isBoss = WaveHasFinalBoss(wave);

        if (isBoss)
        {
            if (bossCount > 1)
                notes.Add(new Note { Text = "Multiple boss waves detected (" + bossCount + "). Exactly one final boss is supported — remove the extra(s).", Type = MessageType.Error });
            else
                notes.Add(new Note { Text = "Boss wave — the final boss is auto-detected in this wave.", Type = MessageType.Info });
        }

        if (WaveHasAroundBoss(wave))
        {
            if (firstBoss < 0)
                notes.Add(new Note { Text = "This wave is set to spawn around the boss, but no wave contains a final boss. Add the boss to an earlier wave.", Type = MessageType.Error });
            else if (index <= firstBoss)
                notes.Add(new Note { Text = "An 'Around Boss' wave must be placed AFTER the boss wave (wave " + firstBoss + "). The boss doesn't exist yet here.", Type = MessageType.Error });
        }

        return notes;
    }

    private static string Suffix(List<Note> notes)
    {
        bool err = false, boss = false;
        foreach (var n in notes)
        {
            if (n.Type == MessageType.Error) err = true;
            if (n.Type == MessageType.Info) boss = true;
        }
        if (err) return "  [⚠ ERROR]";
        if (boss) return "  [BOSS WAVE]";
        return "";
    }

    // ---- GUI ----

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        var notes = BuildNotes(property);

        // Foldout header (carries a status suffix so it's visible even when collapsed).
        var nameProp = property.FindPropertyRelative("Name");
        string title = (nameProp != null && !string.IsNullOrEmpty(nameProp.stringValue)) ? nameProp.stringValue : label.text;
        var header = new GUIContent(title + Suffix(notes));

        var rect = new Rect(position.x, position.y, position.width, LineH);
        property.isExpanded = EditorGUI.Foldout(rect, property.isExpanded, header, true);
        rect.y += LineH + Spacing;

        if (property.isExpanded)
        {
            // Messages first so they read as a banner for the wave.
            foreach (var n in notes)
            {
                var hb = new Rect(rect.x, rect.y, rect.width, MsgH);
                EditorGUI.HelpBox(hb, n.Text, n.Type);
                rect.y += MsgH + Spacing;
            }

            DrawChild(ref rect, property, "Name");
            DrawChild(ref rect, property, "Duration");
            DrawChild(ref rect, property, "KillPercentageToAdvance");
            DrawChild(ref rect, property, "Loop");
            DrawChild(ref rect, property, "AroundBoss");
            DrawChild(ref rect, property, "Groups");
        }

        EditorGUI.EndProperty();
    }

    private static void DrawChild(ref Rect rect, SerializedProperty property, string name)
    {
        var p = property.FindPropertyRelative(name);
        if (p == null) return;
        float h = EditorGUI.GetPropertyHeight(p, true);
        EditorGUI.PropertyField(new Rect(rect.x, rect.y, rect.width, h), p, true);
        rect.y += h + Spacing;
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        float h = LineH + Spacing; // foldout line
        if (!property.isExpanded) return h;

        foreach (var _ in BuildNotes(property))
            h += MsgH + Spacing;

        h += ChildHeight(property, "Name");
        h += ChildHeight(property, "Duration");
        h += ChildHeight(property, "KillPercentageToAdvance");
        h += ChildHeight(property, "Loop");
        h += ChildHeight(property, "AroundBoss");
        h += ChildHeight(property, "Groups");
        return h;
    }

    private static float ChildHeight(SerializedProperty property, string name)
    {
        var p = property.FindPropertyRelative(name);
        if (p == null) return 0f;
        return EditorGUI.GetPropertyHeight(p, true) + Spacing;
    }
}
