using UnityEngine;

/// <summary>
/// Base class for all run upgrades.
/// Concrete upgrades are either <see cref="StatUpgradeSO"/> (player stat upgrades)
/// or <see cref="SpellUpgradeSO"/> (spell unlocks / spell effect upgrades).
/// </summary>
public abstract class UpgradeSO : ScriptableObject
{
    [Header("UI")] public string DisplayName;
    [TextArea(2, 4)] public string Description;
    public Sprite Icon;

    [Header("Core")] public EUpgradeType UpgradeType;

    [Header("Modifier")] public EModiferStrategy ModifierStrategy;
    public float Value;
}
