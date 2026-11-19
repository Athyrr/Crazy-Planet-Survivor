using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Common inspector of every <see cref="InheritableDefinitionSO"/> (EnemySO, BossSO, ...). Without a parent, fields
/// are drawn as usual. With a parent, each field, and each child of a struct field such as CoreStats, gets an
/// "Override" checkbox: unchecked, the value inherited from the parent chain is shown greyed out; checked, this
/// asset's own value is editable and wins at bake time. Arrays are overridden as a whole. A field no ancestor has
/// (Kind of a BossSO under an EnemySO) is always this asset's own value and has no checkbox.
/// </summary>
[CustomEditor(typeof(InheritableDefinitionSO), true)]
public class InheritableDefinitionEditor : UnityEditor.Editor
{
    private const float ToggleWidth = 18f;
    private static readonly GUIContent OverrideToggle = new GUIContent(string.Empty, "Override the parent value");

    private readonly Dictionary<string, bool> _foldouts = new Dictionary<string, bool>();
    private string _pendingPath;
    private bool _pendingOverride;

    public override void OnInspectorGUI()
    {
        if (targets.Length > 1)
        {
            EditorGUILayout.HelpBox("Multi-object editing shows the raw fields, without the inheritance view.", MessageType.Info);
            DrawDefaultInspector();
            return;
        }

        serializedObject.Update();
        var leaf = (InheritableDefinitionSO)target;

        using (new EditorGUI.DisabledScope(true))
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty(InheritableDefinitionSO.ParentFieldName));

        var chain = new List<InheritableDefinitionSO>();
        if (!DefinitionResolver.TryGetChain(leaf, null, chain, out string chainError))
            EditorGUILayout.HelpBox(chainError, MessageType.Error);

        List<string> orphans = DefinitionResolver.GetOrphanPaths(serializedObject);
        if (orphans.Count > 0)
            EditorGUILayout.HelpBox("Overridden paths that match no property (renamed field?): " + string.Join(", ", orphans) +
                                    ". These values are inherited from the parent again.", MessageType.Warning);

        var serializedChain = new List<SerializedObject> { serializedObject };
        for (int i = 1; i < chain.Count; i++)
            serializedChain.Add(new SerializedObject(chain[i]));

        SerializedProperty it = serializedObject.GetIterator();
        bool enterChildren = true;
        while (it.NextVisible(enterChildren))
        {
            enterChildren = false;
            if (DefinitionResolver.IsInheritancePlumbing(it.propertyPath))
                continue;

            if (serializedChain.Count == 1)
                EditorGUILayout.PropertyField(it, true);
            else if (DefinitionResolver.IsExpandedGroup(it))
                DrawGroup(it, serializedChain);
            else
                DrawPath(it, serializedChain);
        }

        // Applied after the loop: copying a value (possibly an array) mid-iteration would invalidate the iterator.
        if (_pendingPath != null)
        {
            DefinitionResolver.SetOverride(serializedObject, _pendingPath, _pendingOverride);
            _pendingPath = null;
        }

        serializedObject.ApplyModifiedProperties();
        for (int i = 1; i < serializedChain.Count; i++)
            serializedChain[i].Dispose();
    }

    private void DrawGroup(SerializedProperty group, List<SerializedObject> chain)
    {
        if (!_foldouts.TryGetValue(group.propertyPath, out bool open))
            open = true;
        open = EditorGUILayout.Foldout(open, group.displayName, true);
        _foldouts[group.propertyPath] = open;
        if (!open)
            return;

        EditorGUI.indentLevel++;
        SerializedProperty child = group.Copy();
        SerializedProperty end = group.GetEndProperty();
        bool enterChildren = true;
        while (child.NextVisible(enterChildren) && !SerializedProperty.EqualContents(child, end))
        {
            enterChildren = false;
            DrawPath(child, chain);
        }
        EditorGUI.indentLevel--;
    }

    private void DrawPath(SerializedProperty own, List<SerializedObject> chain)
    {
        string path = own.propertyPath;
        int source = DefinitionResolver.FindInheritedSourceIndex(chain, path);
        if (source == 0)
        {
            EditorGUILayout.PropertyField(own, true);
            return;
        }

        bool overridden = IsOverridden(serializedObject, path);
        using (new EditorGUILayout.HorizontalScope())
        {
            bool toggled = GUILayout.Toggle(overridden, OverrideToggle, GUILayout.Width(ToggleWidth));
            if (toggled != overridden)
            {
                _pendingPath = path;
                _pendingOverride = toggled;
            }

            if (overridden)
            {
                EditorGUILayout.PropertyField(own, true);
            }
            else
            {
                var label = new GUIContent(own.displayName, $"Inherited from '{chain[source].targetObject.name}'.");
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.PropertyField(chain[source].FindProperty(path), label, true);
            }
        }
    }

    private static bool IsOverridden(SerializedObject leaf, string path)
    {
        SerializedProperty list = leaf.FindProperty(InheritableDefinitionSO.OverriddenPathsFieldName);
        for (int i = 0; i < list.arraySize; i++)
            if (list.GetArrayElementAtIndex(i).stringValue == path)
                return true;
        return false;
    }
}
