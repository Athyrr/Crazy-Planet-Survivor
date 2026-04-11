using Unity.Entities;
using UnityEngine;
using UnityEngine.Scripting;

/// <summary>
/// Contains all entity stats (Base + Accumulated Multipliers).
/// </summary>
[System.Serializable]
public struct CoreStats : IComponentData
{
    [Header("Survival")] [Tooltip("Max Health points.")] [UIStat("Max Health.", "{0}")]
    public float MaxHealth;

    [Tooltip("Health points recovered per second.")] [UIStat("Regen/s", "{0}/s")]
    public float HealthRecovery;

    [Tooltip("Base armor (native to the character).")]
    public float BaseArmor;

    [Tooltip("Damage reduction in % (0 = takes 100% damage, 0.2 = reduces 20% of entering damages).")] [UIStat("Damage Reduc.", "{0:0%}")]
    public float DamageReductionMultiplier;


    [Header("Movement")] [Tooltip("Base movement speed in units/second.")]
    public float BaseMoveSpeed;

    [Tooltip("Speed multiplier (1.0 = 100%).")] [UIStat("Move Sp.", "{0:0%}")]
    public float MoveSpeedMultiplier;

    [Tooltip("Base pickup range.")] public float BasePickupRange;

    [Tooltip("Pickup range multiplier.")] [UIStat("Pickup Range", "{0:0%}")]
    public float PickupRangeMultiplier;


    [Header("Offensive Global Multipliers")]
    [Tooltip("Global damage multiplier (1.0 = normal).")]
    [UIStat("Damage", "{0:0%}")]
    public float GlobalDamageMultiplier;

    [Tooltip("Global cooldown multiplier (0 = 100% du cd, 0.1 = -10% on spell cd).")] [UIStat("Cooldown Reduc.", "{0:0%}")]
    public float GlobalCooldownReductionMultiplier;

    [Tooltip("Area of effect multiplier (Explosions, Zones).")] [UIStat("Area Size", "{0:0%}")]
    public float GlobalSpellAreaMultiplier;

    [Tooltip("Spell Size multiplier (Projectiles).")] [UIStat("Proj. Size", "{0:0%}")]
    public float GlobalSpellSizeMultiplier;

    [Tooltip("Projectile speed multiplier.")] [UIStat("Spell Sp.", "{0:0%}")]
    public float GlobalSpellSpeedMultiplier;

    [Tooltip("Spell duration multiplier.")] [UIStat("Spell Duration", "{0:0%}")]
    public float GlobalSpellDurationMultiplier;

    [Tooltip("Cast range multiplier (for targeted spells).")] [UIStat("Cast Range", "{0:0%}")]
    public float GlobalCastRangeMultiplier;


    [Header("Offensive Global Bonuses")]
    [Tooltip("Number of projectiles added to all spells.")]
    [UIStat("Amount", "+{0}")]
    public int GlobalAmountBonus;

    [Tooltip("Number of pierces added to all projectiles.")] [UIStat("Pierce", "+{0}")]
    public int GlobalPierceBonus;

    [Tooltip("Number of bounces added to all projectiles.")] [UIStat("Bounce", "+{0}")]
    public int GlobalBounceBonus;


    [Header("Critical")] [Tooltip("Critical chance probability (0.0 to 1.0).")] [UIStat("Crit Chance", "{0:0%}")]
    public float CritChance;

    [Tooltip("Critical damage multiplier (1.5 = 150%).")] [UIStat("Crit Damage", "{0:0%}")]
    public float CritDamageMultiplier;
}