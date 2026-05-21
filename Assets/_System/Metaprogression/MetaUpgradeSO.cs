using _System.ECS.Authorings.Resources;
using UnityEngine;

/// <summary>
/// Defines a single meta-progression upgrade line for a character stat.
/// Each upgrade can be leveled up to 5 times, giving permanent starting bonuses.
/// </summary>
[CreateAssetMenu(menuName = "Game/Meta Progression/Meta Upgrade")]
public class MetaUpgradeSO : ScriptableObject
{
    [Header("Display")]
    public string DisplayName;
    [TextArea(2, 4)] public string Description;
    public Sprite Icon;

    [Header("Target")]
    public ECharacterStat TargetStat;

    [Header("Levels (max 5)")]
    [Tooltip("Bonus value per level (delta format: 0.1 = +10%). Must have exactly 5 entries.")]
    public float[] BonusPerLevel = new float[5];

    [Tooltip("Purchase cost per level. Must have exactly 5 entries.")]
    public ResourceCost[] CostPerLevel = new ResourceCost[5];

    /// <summary>
    /// Gets the cumulative bonus for the given level.
    /// </summary>
    public float GetTotalBonus(int level)
    {
        if (level <= 0) return 0f;

        float total = 0f;
        int capped = Mathf.Min(level, BonusPerLevel.Length);
        for (int i = 0; i < capped; i++)
            total += BonusPerLevel[i];

        return total;
    }

    /// <summary>
    /// Gets the bonus gained specifically at this level (1-indexed).
    /// </summary>
    public float GetLevelBonus(int level)
    {
        int idx = level - 1;
        if (idx < 0 || idx >= BonusPerLevel.Length)
            return 0f;

        return BonusPerLevel[idx];
    }

    /// <summary>
    /// Gets the cost for reaching the next level (1-indexed).
    /// </summary>
    public ResourceCost[] GetLevelCost(int level)
    {
        int idx = level - 1;
        if (idx < 0 || idx >= CostPerLevel.Length)
            return null;

        return CostPerLevel[idx].Amount > 0 ? new[] { CostPerLevel[idx] } : null;
    }

    /// <summary>
    /// Returns the resource cost for the next level, or null if maxed.
    /// Returns the cost at index matching currentLevel (0-based).
    /// </summary>
    public ResourceCost GetCostForLevel(int currentLevel)
    {
        if (currentLevel < 0 || currentLevel >= CostPerLevel.Length)
            return default;

        return CostPerLevel[currentLevel];
    }
}
