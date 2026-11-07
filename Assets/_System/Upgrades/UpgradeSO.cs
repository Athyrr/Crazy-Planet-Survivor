using UnityEngine;

/// <summary>
/// Base class for all run upgrades.
/// Concrete upgrades are either <see cref="CharacterStatUpgradeSO"/> (player stat upgrades)
/// or <see cref="SpellUpgradeSO"/> (spell unlocks / spell effect upgrades).
/// </summary>
public abstract class UpgradeSO : ScriptableObject
{
    [Header("UI")] public string DisplayName;
    [TextArea(2, 4)] public string Description;
    public Sprite Icon;

    [Header("Core")] public EUpgradeType UpgradeType;

    /// <summary>Additive delta: 0.1 = +10% on a multiplier stat, 5 = +5 on an absolute (integer) stat.</summary>
    [Header("Modifier")] public float Value;
}
