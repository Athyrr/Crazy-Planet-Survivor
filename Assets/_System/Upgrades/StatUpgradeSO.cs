using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// A single stat modification (bonus or malus) applied by a <see cref="StatUpgradeSO"/>.
/// A stat upgrade can bundle several of these (e.g. +Damage but -MoveSpeed).
/// </summary>
[System.Serializable]
public struct StatModifier
{
    [Tooltip("Player stat to modify.")]
    public ECharacterStat CharacterStat;

    [Tooltip("How Value is applied to the stat (Flat = +Value, Multiply = *Value).")]
    public EModiferStrategy Strategy;

    [Tooltip("Modifier value. Can be negative for a malus.")]
    public float Value;
}

/// <summary>
/// A run upgrade that modifies one or more player character stats (Damage, Speed, MaxHealth...).
/// Carries a <see cref="ERarity"/> tier that drives drop chance and crystal visuals.
/// </summary>
[CreateAssetMenu(fileName = "NewStatUpgrade", menuName = "Survivor/Upgrades/Stat Upgrade")]
public class StatUpgradeSO : UpgradeSO
{
    [Header("Rarity")]
    [Tooltip("Rarity tier. Rarer upgrades drop less often (weighted by the Luck stat).")]
    public ERarity Rarity = ERarity.Common;

    [Header("Modifiers (bonus / malus)")]
    [Tooltip("One or more stat modifications applied together when this upgrade is picked.")]
    public StatModifier[] Modifiers = new StatModifier[0];

    // Legacy single-stat data (pre multi-modifier). Captured for the migration menu only.
    [HideInInspector] [SerializeField] [FormerlySerializedAs("CharacterStat")]
    private ECharacterStat _legacyCharacterStat = ECharacterStat.None;

    /// <summary>Old single-stat field, kept only so the migration tool can fold it into Modifiers.</summary>
    public ECharacterStat LegacyCharacterStat => _legacyCharacterStat;

    /// <summary>Clears the legacy single-stat data once it has been migrated into Modifiers.</summary>
    public void ClearLegacyStat() => _legacyCharacterStat = ECharacterStat.None;

    private void OnValidate()
    {
        // A stat upgrade is always a PlayerStat upgrade.
        UpgradeType = EUpgradeType.PlayerStat;
    }
}
