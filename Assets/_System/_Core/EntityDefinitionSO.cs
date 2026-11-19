using UnityEngine;

/// <summary>
/// Opt-in base of entity definition ScriptableObjects (spells, characters, summons, enemies): the identity shown
/// in the UI. Field names match the pre-existing ones, so serialized data is preserved. Concrete classes keep
/// their own prefab and stats fields.
/// </summary>
public abstract class EntityDefinitionSO : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("Name displayed in the UI.")]
    public string DisplayName = string.Empty;

    [Tooltip("Icon displayed in the UI.")]
    public Sprite Icon;

    [TextArea(2, 4)] [Tooltip("Description displayed in the UI.")]
    public string Description;
}
