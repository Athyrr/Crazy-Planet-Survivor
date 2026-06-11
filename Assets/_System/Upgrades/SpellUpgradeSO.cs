using UnityEngine;

/// <summary>
/// A run upgrade that targets spells: either unlocks a new spell (<see cref="EUpgradeType.UnlockSpell"/>)
/// or upgrades an existing spell / tagged spells (<see cref="EUpgradeType.UpgradeSpell"/>).
/// </summary>
[CreateAssetMenu(fileName = "NewSpellUpgrade", menuName = "Survivor/Upgrades/Spell Upgrade")]
public class SpellUpgradeSO : UpgradeSO
{
    [Header("Target Spell")]
    [Tooltip("Spell to unlock or upgrade. None if we target all tagged spells.")]
    public ESpellID SpellID;

    [Header("Target Tags")]
    [Tooltip("Spell Tags to upgrade a spell. E.g. 'Fire' will upgrade all Fire tagged spells." +
             "\n Note that if SpellID is set, the tags will be added as new tags to the spell.")]
    public ESpellTag RequiredTags;

    [Header("Upgrade")]
    [Tooltip("Property of the spell to modify (Damage, Cooldown, Amount...).")]
    public ESpellStat SpellStat;

    private void OnValidate()
    {
        // A spell upgrade is never a PlayerStat upgrade: it either unlocks or upgrades a spell.
        if (UpgradeType == EUpgradeType.PlayerStat)
            UpgradeType = EUpgradeType.UpgradeSpell;
    }
}
