using Unity.Entities;
using UnityEngine;

/// <summary>
/// Represents all stats an entity has during a run. 
/// Make sure that <see cref="Stats"/> elements correspond to <see cref="EStatID"/> + <see cref="BaseStats"/> elements.
/// </summary>
public struct Stats : IComponentData
{
    [Header("Survival")]
    public float MaxHealth;
    public float Armor;

    [Header("Movement")]
    public float MoveSpeed;

    [Header("Offensive")]
    public float Damage;
    public float CooldownReduction;

    [Header("Spell Modifiers")]
    public float ProjectileSpeedMultiplier;
    public float EffectAreaRadiusMult;
    public int BouncesAdded;
    public int PierceAdded;

    [Header("Resistances (%)")]
    public float FireResistance;
    public float IceResistance;
    public float LightningResistance;
    public float ArcaneResistance;

    [Header("Utility")]
    public float CollectRange;
    public float MaxCollectRange;
}

/// <summary>
/// Represents base stats of an entity (Initial values).
/// Make sure that <see cref="BaseStats"/> elements correspond to <see cref="EStatID"/> + <see cref="Stats"/> elements.
/// </summary>
[System.Serializable]
public struct BaseStats : IComponentData
{
    [Header("Survival")]

    [Tooltip("Maximum health points of the entity.")]
    public float MaxHealth;

    [Tooltip("Flat damage reduction applied to incoming attacks.")]
    public float Armor;

    [Header("Movement")]
    [Tooltip("Movement speed in units per second.")]
    public float MoveSpeed;

    [Header("Offensive")]
    [Tooltip("Base damage bonus added to spells/attacks.")]
    public float Damage;

    [Tooltip("Percentage reduction of spell cooldowns (0.0 = 0%, 0.5 = 50%).")]
    public float CooldownReduction;


    [Header("Spell Modifiers")]

    [Tooltip("Multiplier applied to the base speed of projectiles (1.0 = normal speed).")]
    public float ProjectileSpeedMultiplier;

    [Tooltip("Multiplier applied to the radius of Area of Effect spells.")]
    public float EffectAreaRadiusMultiplier;

    [Tooltip("Number of additional bounces for ricochet projectiles.")]
    public int BouncesAdded;

    [Tooltip("Number of additional enemies a projectile can pass through.")]
    public int PierceAdded;


    [Header("Resistances (%)")]

    [Tooltip("Percentage resistance to Fire damage.")]
    [Range(0, 80)]
    public float FireResistance;

    [Tooltip("Percentage resistance to Ice damage.")]
    [Range(0, 80)]
    public float IceResistance;

    [Tooltip("Percentage resistance to Lightning damage.")]
    [Range(0, 80)]
    public float LightningResistance;

    [Tooltip("Percentage resistance to Arcane damage.")]
    [Range(0, 80)]
    public float ArcaneResistance;


    [Header("Utility")]

    [Tooltip("Current radius for picking up items and XP orbs.")]
    [Range(1, 50)]
    public float CollectRange;

    [Tooltip("The hard cap limit for the collect range.")]
    [Range(1, 50)]
    public float MaxCollectRange;
}