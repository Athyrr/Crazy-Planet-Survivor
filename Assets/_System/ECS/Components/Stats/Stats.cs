using Unity.Entities;
using UnityEngine;
using UnityEngine.Scripting;

/// <summary>
/// Contains all entity stats (Base + Accumulated Multipliers).
/// </summary>
[System.Serializable]
[Preserve]
public struct Stats : IComponentData
{
    [Header("Survival")]
    [Tooltip("Base Health points (before multipliers).")]
    [UIStat("Base Health", "{0:0}")]
    public float BaseMaxHealth;

    [Tooltip("Health multiplier (1.0 = 100%). Increases with 'Max Health %' upgrades.")]
    [UIStat("Health Mult.", "{0:0%}")] // Displays x100%
    public float MaxHealthMultiplier; 

    [Tooltip("Health points recovered per second.")]
    [UIStat("Regen", "{0:0.0}/s")]
    public float HealthRecovery;

    [Tooltip("Base armor (native to the character).")]
    public float BaseArmor;

    [Tooltip("Damage reduction in % (0.9 = takes 90% damage).")]
    [UIStat("Dmg Reduc.", "{0:0%}")]
    public float DamageReductionMultiplier;


    [Header("Movement")]
    [Tooltip("Base movement speed in units/second.")]
    [UIStat("Base Speed", "{0:0}")]
    public float BaseMoveSpeed;

    [Tooltip("Speed multiplier (1.0 = 100%).")]
    [UIStat("Speed Mult.", "{0:0%}")]
    public float MoveSpeedMultiplier;

    [Tooltip("Base pickup range.")]
    public float BasePickupRange;

    [Tooltip("Pickup range multiplier.")]
    [UIStat("Pickup Range", "{0:0%}")]
    public float PickupRangeMultiplier;


    [Header("Offensive Global Multipliers")]
    
    [Tooltip("Global damage multiplier (1.0 = normal).")]
    [UIStat("Global Dmg", "{0:0%}")]
    public float GlobalDamageMultiplier;

    [Tooltip("Global cooldown multiplier (1.0 = normal, 0.9 = -10%).")]
    [UIStat("Cooldown", "{0:0%}")]
    public float GlobalCooldownMultiplier;

    [Tooltip("Area of effect multiplier (Explosions, Zones).")]
    [UIStat("Area Size", "{0:0%}")]
    public float GlobalSpellAreaMultiplier;

    [Tooltip("Physical size multiplier (Projectiles).")]
    [UIStat("Proj. Size", "{0:0%}")]
    public float GlobalSpellSizeMultiplier;

    [Tooltip("Projectile speed multiplier.")]
    [UIStat("Proj. Speed", "{0:0%}")]
    public float GlobalSpellSpeedMultiplier;

    [Tooltip("Spell duration multiplier.")]
    [UIStat("Duration", "{0:0%}")]
    public float GlobalDurationMultiplier;
    

    [Header("Offensive Global Bonuses")]
    
    [Tooltip("Number of projectiles added to all spells.")]
    [UIStat("Amount +", "+{0}")]
    public int GlobalAmountBonus;

    [Tooltip("Number of pierces added to all projectiles.")]
    [UIStat("Pierce +", "+{0}")]
    public int GlobalPierceBonus;

    [Tooltip("Number of bounces added to all projectiles.")]
    [UIStat("Bounce +", "+{0}")]
    public int GlobalBounceBonus;


    [Header("Critical")]
    [Tooltip("Critical chance probability (0.0 to 1.0).")]
    [UIStat("Crit Chance", "{0:0%}")]
    public float CritChance;

    [Tooltip("Critical damage multiplier (1.5 = 150%).")]
    [UIStat("Crit Dmg", "x{0:0.0}")]
    public float CritDamageMultiplier;
}
