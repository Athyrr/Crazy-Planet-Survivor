using System;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;
using Object = UnityEngine.Object;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// A resolved definition. <see cref="Value"/> is either the asset itself (no parent) or an in-memory copy with the
/// parent chain applied; <see cref="Dispose"/> destroys the copy, never the asset. Use it in a using statement and
/// read it before the end of the scope.
/// </summary>
public readonly struct ResolvedDefinition<T> : IDisposable where T : InheritableDefinitionSO
{
    public readonly T Value;
    private readonly bool _isCopy;

    public ResolvedDefinition(T value, bool isCopy)
    {
        Value = value;
        _isCopy = isCopy;
    }

    public void Dispose()
    {
        if (_isCopy && Value != null)
            Object.DestroyImmediate(Value);
    }
}

/// <summary>
/// Resolves an <see cref="InheritableDefinitionSO"/> through its parent chain. For every overridable property
/// path not overridden by the leaf, the value comes from the nearest ancestor that overrides it, otherwise from
/// the farthest ancestor that has the property (the root of the chain for that path). An ancestor that lacks the
/// property (an EnemySO parent of a BossSO, for Kind) is skipped. Called by bakers (with their IBaker, so DependsOn
/// is declared on every link of the chain) and by editor code (baker = null). Runtime code never calls it.
/// </summary>
public static class DefinitionResolver
{
    /// <summary>Fills <paramref name="chain"/> leaf first. Declares DependsOn on every member, including the one
    /// that closes a loop. Returns false on a loop (or a self parent), the chain being cut before the repeat.</summary>
    public static bool TryGetChain(InheritableDefinitionSO leaf, IBaker baker, List<InheritableDefinitionSO> chain, out string error)
    {
        chain.Clear();
        error = null;
        var visited = new HashSet<InheritableDefinitionSO>();
        for (InheritableDefinitionSO node = leaf; node != null; node = node.ParentDefinition)
        {
            baker?.DependsOn(node);
            if (!visited.Add(node))
            {
                error = node == leaf && chain.Count == 1
                    ? $"'{leaf.name}' is its own parent."
                    : $"Parent loop in the chain of '{leaf.name}' (at '{node.name}').";
                return false;
            }
            chain.Add(node);
        }
        return true;
    }

    public static ResolvedDefinition<T> Resolve<T>(T leaf, IBaker baker) where T : InheritableDefinitionSO
    {
        if (leaf == null)
            return default;

        var chain = new List<InheritableDefinitionSO>();
        if (!TryGetChain(leaf, baker, chain, out string error))
            Debug.LogError($"[DefinitionResolver] {error} Resolving with the chain cut before the loop.", leaf);

        if (chain.Count == 1)
            return new ResolvedDefinition<T>(leaf, false);

#if UNITY_EDITOR
        T copy = Object.Instantiate(leaf);
        copy.name = leaf.name;
        copy.hideFlags = HideFlags.HideAndDontSave;

        var serializedChain = new List<SerializedObject>(chain.Count);
        foreach (InheritableDefinitionSO node in chain)
            serializedChain.Add(new SerializedObject(node));

        using (var target = new SerializedObject(copy))
        {
            foreach (string path in GetOverridablePaths(serializedChain[0]))
            {
                if (leaf.IsOverridden(path))
                    continue;
                int source = FindInheritedSourceIndex(serializedChain, path);
                if (source > 0)
                    target.CopyFromSerializedProperty(serializedChain[source].FindProperty(path));
            }
            target.ApplyModifiedPropertiesWithoutUndo();
        }

        foreach (SerializedObject so in serializedChain)
            so.Dispose();
        return new ResolvedDefinition<T>(copy, true);
#else
        // Never reached: only bakers and editor code resolve definitions, and both run in the editor.
        return new ResolvedDefinition<T>(leaf, false);
#endif
    }

#if UNITY_EDITOR
    /// <summary>Properties that carry the inheritance itself, never inherited nor overridable.</summary>
    public static bool IsInheritancePlumbing(string path) =>
        path == "m_Script" || path == InheritableDefinitionSO.ParentFieldName ||
        path == InheritableDefinitionSO.OverriddenPathsFieldName;

    /// <summary>A struct field (e.g. CoreStats) whose direct children are overridden one by one. Arrays are not.</summary>
    public static bool IsExpandedGroup(SerializedProperty property) =>
        property.propertyType == SerializedPropertyType.Generic && !property.isArray && property.hasVisibleChildren;

    /// <summary>Every overridable path of a definition: visible top-level fields, struct fields expanded one level.</summary>
    public static List<string> GetOverridablePaths(SerializedObject definition)
    {
        var paths = new List<string>();
        SerializedProperty it = definition.GetIterator();
        bool enterChildren = true;
        while (it.NextVisible(enterChildren))
        {
            enterChildren = false;
            if (IsInheritancePlumbing(it.propertyPath))
                continue;
            if (!IsExpandedGroup(it))
            {
                paths.Add(it.propertyPath);
                continue;
            }
            SerializedProperty child = it.Copy();
            SerializedProperty end = it.GetEndProperty();
            bool enterChild = true;
            while (child.NextVisible(enterChild) && !SerializedProperty.EqualContents(child, end))
            {
                enterChild = false;
                paths.Add(child.propertyPath);
            }
        }
        return paths;
    }

    /// <summary>Index (>= 1) of the ancestor that supplies <paramref name="path"/> when the leaf (index 0) does not
    /// override it: the nearest overriding ancestor, else the farthest ancestor that has the property. 0 when no
    /// ancestor has it (the leaf's own value is always used).</summary>
    public static int FindInheritedSourceIndex(IReadOnlyList<SerializedObject> chain, string path)
    {
        int source = 0;
        for (int i = 1; i < chain.Count; i++)
        {
            if (chain[i].FindProperty(path) == null)
                continue; // ancestor lacking the property: skipped
            source = i;
            if (((InheritableDefinitionSO)chain[i].targetObject).IsOverridden(path))
                return i;
        }
        return source;
    }

    /// <summary>Entries of OverriddenPaths that match no overridable property any more (renamed field).</summary>
    public static List<string> GetOrphanPaths(SerializedObject definition)
    {
        var known = new HashSet<string>(GetOverridablePaths(definition));
        SerializedProperty list = definition.FindProperty(InheritableDefinitionSO.OverriddenPathsFieldName);
        var orphans = new List<string>();
        for (int i = 0; i < list.arraySize; i++)
        {
            string path = list.GetArrayElementAtIndex(i).stringValue;
            if (!known.Contains(path))
                orphans.Add(path);
        }
        return orphans;
    }

    /// <summary>Turns the override of <paramref name="path"/> on (copying the inherited value into the leaf first)
    /// or off (keeping the leaf's own value, ignored from now on). The caller applies the modified properties.</summary>
    public static void SetOverride(SerializedObject leaf, string path, bool overridden)
    {
        SerializedProperty list = leaf.FindProperty(InheritableDefinitionSO.OverriddenPathsFieldName);
        int index = -1;
        for (int i = 0; i < list.arraySize; i++)
            if (list.GetArrayElementAtIndex(i).stringValue == path) { index = i; break; }

        if (!overridden)
        {
            if (index >= 0)
                list.DeleteArrayElementAtIndex(index);
            return;
        }
        if (index >= 0)
            return;

        var chain = new List<InheritableDefinitionSO>();
        TryGetChain((InheritableDefinitionSO)leaf.targetObject, null, chain, out _);
        var serializedChain = new List<SerializedObject> { leaf };
        for (int i = 1; i < chain.Count; i++)
            serializedChain.Add(new SerializedObject(chain[i]));

        int source = FindInheritedSourceIndex(serializedChain, path);
        if (source > 0)
            leaf.CopyFromSerializedProperty(serializedChain[source].FindProperty(path));

        list = leaf.FindProperty(InheritableDefinitionSO.OverriddenPathsFieldName);
        list.arraySize++;
        list.GetArrayElementAtIndex(list.arraySize - 1).stringValue = path;

        for (int i = 1; i < serializedChain.Count; i++)
            serializedChain[i].Dispose();
    }
#endif
}
