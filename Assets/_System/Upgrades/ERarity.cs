/// <summary>
/// Rarity tier of a stat upgrade. Order matters: higher value = rarer.
/// Only <see cref="StatUpgradeSO"/> upgrades carry a rarity; spell upgrades/unlocks do not.
/// Drop weights, crystal materials, labels and colors are configured per tier in CpRaritySettings.
/// </summary>
public enum ERarity
{
    Common = 0,
    Uncommon = 1,
    Rare = 2,
    Epic = 3,
    Legendary = 4,
}

public static class RarityConstants
{
    /// <summary>Number of rarity tiers (keep in sync with <see cref="ERarity"/>).</summary>
    public const int Count = 5;
}
