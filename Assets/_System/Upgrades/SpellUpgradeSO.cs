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

    [Header("Granted Effects")]
    [Tooltip("Status effects this upgrade grants to the target spell (e.g. \"Fireball now burns\"). Only " +
             "meaningful when SpellID is set. Independent of RequiredTags above: RequiredTags still gates " +
             "which spells this upgrade targets (and, when SpellID is set, still adds any BEHAVIOR/form tags " +
             "bundled in the same value — Explosive/Piercing/Bouncing, untouched by this chantier); " +
             "GrantedEffects grants actual effect DATA that SpellStatsCalculationSystem composes into the " +
             "spell's effective effect list — see spec §4.3 correction.")]
    public EffectSpec[] GrantedEffects = new EffectSpec[0];

    private void OnValidate()
    {
        // A spell upgrade is never a PlayerStat upgrade: it either unlocks or upgrades a spell.
        if (UpgradeType == EUpgradeType.PlayerStat)
            UpgradeType = EUpgradeType.UpgradeSpell;
    }
}
