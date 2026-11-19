using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Non-generic base of an inheritable definition, so the resolver and the common inspector can walk the parent
/// chain without knowing the concrete type. Values are plain serialized fields: a property path listed in
/// <see cref="OverriddenPaths"/> (a top-level field, or a child of a struct field such as "BaseStats.MaxHealth";
/// arrays as a whole) keeps this asset's own value, every other one is taken from the parent chain at bake time
/// by <see cref="DefinitionResolver"/>. An inheritable definition is never read as-is at runtime.
/// </summary>
public abstract class InheritableDefinitionSO : EntityDefinitionSO
{
    public const string ParentFieldName = "Parent";
    public const string OverriddenPathsFieldName = nameof(OverriddenPaths);

    [HideInInspector]
    [Tooltip("Serialized property paths whose value is this asset's own (e.g. 'BaseStats.MaxHealth', 'InitialSpells').")]
    public List<string> OverriddenPaths = new List<string>();

    /// <summary>The parent definition, or null for a root.</summary>
    public abstract InheritableDefinitionSO ParentDefinition { get; }

    public bool IsOverridden(string path) => OverriddenPaths != null && OverriddenPaths.Contains(path);
}

/// <summary>Typed parent: an EnemySO's parent is an EnemySO (or any subclass, e.g. a BossSO).</summary>
public abstract class InheritableDefinitionSO<T> : InheritableDefinitionSO where T : InheritableDefinitionSO<T>
{
    [Tooltip("Optional parent. Every value not overridden here comes from the parent chain at bake time.")]
    public T Parent;

    public override InheritableDefinitionSO ParentDefinition => Parent;
}
