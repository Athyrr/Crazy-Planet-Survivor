using Unity.Entities;

/// <summary>
/// Baked representation of an <see cref="UpgradeSO"/>.
/// Stat upgrades use <see cref="StatModifiers"/> + <see cref="Rarity"/>;
/// spell upgrades/unlocks use the spell fields below (and ignore rarity).
/// </summary>
public struct UpgradeBlob
{
    public BlobString DisplayName;
    public BlobString Description;

    public EUpgradeType UpgradeType;

    // --- Stat upgrade (EUpgradeType.PlayerStat) ---
    public ERarity Rarity;
    public BlobArray<StatModifierBlob> StatModifiers;

    // --- Spell upgrade / unlock (EUpgradeType.UpgradeSpell / UnlockSpell) ---
    public ESpellID SpellID;
    public ESpellTag SpellTags;
    public ESpellStat SpellStat;
    public EModiferStrategy ModifierStrategy;
    public float Value;
}

/// <summary>
/// Baked representation of a <see cref="StatModifier"/> (one bonus/malus of a stat upgrade).
/// </summary>
public struct StatModifierBlob
{
    public ECharacterStat CharacterStat;
    public EModiferStrategy Strategy;
    public float Value;
}
