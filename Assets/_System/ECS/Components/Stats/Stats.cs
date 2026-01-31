using UnityEngine.Scripting;
using Unity.Entities;
using UnityEngine;

/// <summary>
/// Represents all stats an entity has during a run. 
/// Make sure that <see cref="Stats"/> elements correspond to <see cref="ECharacterStat"/> + <see cref="BaseStats"/> elements.
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
/// Make sure that <see cref="BaseStats"/> elements correspond to <see cref="ECharacterStat"/> + <see cref="Stats"/> elements.
/// </summary>
[System.Serializable]
[Preserve] // preserve string format reflexion on build
public struct BaseStats : IComponentData
{
    [Header("Survival")]

    [Tooltip("Maximum health points of the entity.")]
    [UIStat("Max Health", "{0:0}")]
    public float MaxHealth;

    [Tooltip("Flat damage reduction applied to incoming attacks.")]
    [UIStat("Armor", "{0:0}")]
    public float Armor;

    [Header("Movement")]
    [Tooltip("Movement Sp. in units per second.")]
    [UIStat("Move Sp.", "{0:0}")]
    public float MoveSpeed;

    [Header("Offensive")]
    [Tooltip("Base damage bonus added to spells/attacks.")]
    [UIStat("Damage", "{0:0}")]
    public float Damage;

    [Tooltip("Percentage reduction of spell cooldowns (0.0 = 0%, 0.5 = 50%).")]
    [UIStat("Cooldown Reduc.", "-{0:0\u0025}")]
    [StepRange(0, 1, 0.01f)]
    public float CooldownReduction;


    [Header("Spell Modifiers")]

    [Tooltip("Multiplier applied to the base speed of projectiles (1.0 = normal speed).")]
    [UIStat("Proj. Sp.", "{0:+0\u0025;-0\u0025;0\u0025}")]
    public float ProjectileSpeedMultiplier;

    [Tooltip("Multiplier applied to the radius of Area of Effect spells.")]
    [UIStat("AoE Radius", "{0:+0\u0025;-0\u0025;0\u0025}")]// c'est {0:0%} mais % marche pas
    public float EffectAreaRadiusMultiplier;

    [Tooltip("Number of additional bounces for ricochet projectiles.")]
    [UIStat("Add. Bounces")]
    public int BouncesAdded;

    [Tooltip("Number of additional enemies a projectile can pass through.")]
    [UIStat("Add. Pierces")]
    public int PierceAdded;


    [Header("Resistances (%)")]

    [Tooltip("Percentage resistance to Fire damage.")]
    [UIStat("Fire Res.", "{0:0\u0025}")]
    [StepRange(0, 0.8f, 0.01f)]
    public float FireResistance;

    [Tooltip("Percentage resistance to Ice damage.")]
    [UIStat("Ice Res.", "{0:0\u0025}")]
    [StepRange(0, 0.8f, 0.01f)]
    public float IceResistance;

    [Tooltip("Percentage resistance to Lightning damage.")]
    [UIStat("Lightning Res.", "{0:0\u0025}")]
    [StepRange(0, 0.8f, 0.01f)]
    public float LightningResistance;

    [Tooltip("Percentage resistance to Arcane damage.")]
    [UIStat("Arcane Res.", "{0:0\u0025}")]
    [StepRange(0, 0.8f, 0.01f)]
    public float ArcaneResistance;


    [Header("Utility")]

    [Tooltip("Current radius for picking up items and XP orbs.")]
    [UIStat("Collect Range", "{0:0.0}")]
    [Range(1, 50)]
    public float CollectRange;

    [Tooltip("The hard cap limit for the collect range.")]
    [UIStat("Max Collect", "{0:0.0}")]
    [Range(1, 50)]
    public float MaxCollectRange;
}