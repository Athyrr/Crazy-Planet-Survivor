using Unity.Entities;
using UnityEngine;
using UnityEngine.Scripting;

/// <summary>
/// Contains all entity stats (Base + Accumulated Multipliers).
/// </summary>
[System.Serializable]
public struct CoreStats : IComponentData
{
    [Header("Survival")]
    [Tooltip("Max Health points.")]
    [UIStat("Max Health.", ECharacterStat.MaxHealth, absolute: true)]
    public float MaxHealth;

    [Tooltip("Health points recovered per second.")]
    [UIStat("Regen/s", ECharacterStat.HealthRegen, absolute: true, suffix: "/s", decimals: 1)]
    public float HealthRegen;

    [Tooltip("Base armor (native to the character).")] //todo
    public float BaseArmor;

    [Tooltip("Flat damage reduction in % (0 = none, 0.2 = 20% less damage taken).")]
    [UIStat("Armor", ECharacterStat.Armor)]
    public float Armor;

    [Header("Movement")]
    [Tooltip("Base movement speed in units/second.")] //todo
    public float BaseMoveSpeed;

    [Tooltip("Speed bonus (0 = 100%, 0.1 = +10%).")]
    [UIStat("Move Sp.", ECharacterStat.Speed)]
    public float MoveSpeed;

    [Tooltip("Base pickup range.")]
    public float BasePickupRange;

    [Tooltip("Pickup range bonus.")]
    [UIStat("Pickup Range", ECharacterStat.CollectRange)]
    public float PickupRange;

    [Header("Offensive Global Multipliers")]
    [Tooltip("Damage bonus (0 = normal, 0.5 = +50%).")]
    [UIStat("Damage", ECharacterStat.Damage)]
    public float Damage;

    [Tooltip("Attack speed bonus (0 = 100%, 0.1 = +10%).")]
    [UIStat("Attack Sp.", ECharacterStat.AttackSpeed)]
    public float AttackSpeed;

    [Tooltip("Spell size bonus (projectiles).")]
    [UIStat("Proj. Size", ECharacterStat.SizeMultiplier)]
    public float SpellSize;

    [Tooltip("Projectile speed bonus.")]
    [UIStat("Spell Sp.", ECharacterStat.SpellSpeed)]
    public float SpellSpeed;

    [Tooltip("Spell duration bonus.")]
    [UIStat("Spell Duration", ECharacterStat.SpellDuration)]
    public float SpellDuration;

    [Tooltip("Cast range bonus (for targeted spells).")]
    [UIStat("Cast Range", ECharacterStat.CastRange)]
    public float CastRange;

    [Header("Offensive Global Bonuses")]
    [Tooltip("Number of projectiles added to all spells.")]
    [UIStat("Amount", ECharacterStat.Amount)]
    public int Amount;

    [Tooltip("Number of pierces added to all projectiles.")]
    [UIStat("Pierce", ECharacterStat.PierceCount)]
    public int Pierce;

    [Tooltip("Number of bounces added to all projectiles.")]
    [UIStat("Bounce", ECharacterStat.BounceCount)]
    public int Bounce;

    [Header("Critical")]
    [Tooltip("Critical chance probability (0.0 to 1.0).")]
    [UIStat("Crit Chance", ECharacterStat.CritChance)]
    public float CritChance;

    [Tooltip("Bonus critical damage (0 = normal, 0.5 = +50%).")]
    [UIStat("Crit Damage", ECharacterStat.CritDamage)]
    public float CritDamage;

    [Header("Luck")]
    [Tooltip("Increases the chance of drawing rarer stat upgrades on level up (see CpRaritySettings luck curve).")]
    [UIStat("Luck", ECharacterStat.Luck, absolute: true)]
    public float Luck;
}
