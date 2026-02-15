using UnityEngine;

[CreateAssetMenu(fileName = "NewUpgradeSO", menuName = "Survivor/Upgrades/Upgrade")]
public class UpgradeSO : ScriptableObject
{
    [Header("UI")]
    public string DisplayName;
    [TextArea(2, 4)]
    public string Description;
    public Sprite Icon;

    [Header("Core")]
    public EUpgradeType UpgradeType;

    [Header("Character Stat Upgrade")]
    public ECharacterStat CharacterStat;

    [Header("Target Spell")]
    [Tooltip("Spell to unlock or upgrade.None if we target all tagged spell.")]
    public ESpellID SpellID;

    [Header("Target Tags")]
    [Tooltip("Spell Tags to upgrade a spell. E.g. 'Fire' will upgrade all Fire tagged spells.")]
    public ESpellTag RequiredTags;

    [Header("Upgrade")]
    [Tooltip("Property of the spell to modify (Damage, Cooldown, Amount...).")]
    public ESpellStat SpellStat;
    public EStatModiferStrategy ModifierStrategy;
    public float Value;
}
