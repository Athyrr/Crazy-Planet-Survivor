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

    [Tooltip("Health points recovered per second.")] [UIStat("Regen/s", "{0:0.0}/s")]
    public float HealthRecovery;

    [Tooltip("Base armor (native to the character).")]
    public float BaseArmor;

    [Tooltip("Flat damage reduction in % (0 = none, 0.2 = 20% less damage taken).")] [UIStat("Armor", "{0:+0%;-0%;0}")]
    public float Armor;


    [Header("Movement")] [Tooltip("Base movement speed in units/second.")]
    public float BaseMoveSpeed;

    [Tooltip("Speed bonus (0 = 100%, 0.1 = +10%).")] [UIStat("Move Sp.", "{0:+0%;-0%;0}")]
    public float MoveSpeed;

    [Tooltip("Base pickup range.")] public float BasePickupRange;

    [Tooltip("Pickup range bonus.")] [UIStat("Pickup Range", "{0:+0%;-0%;0}")]
    public float PickupRange;


    [Header("Offensive Global Multipliers")]
    [Tooltip("Damage bonus (0 = normal, 0.5 = +50%).")]
    [UIStat("Damage", "{0:+0%;-0%;0}")]
    public float Damage;

    [Tooltip("Attack speed bonus (0 = 100%, 0.1 = +10%).")]
    [UIStat("Attack Sp.", "{0:+0%;-0%;0}")]
    public float AttackSpeed;

    [Tooltip("Spell size bonus (projectiles).")] [UIStat("Proj. Size", "{0:+0%;-0%;0}")]
    public float SpellSize;

    [Tooltip("Projectile speed bonus.")] [UIStat("Spell Sp.", "{0:+0%;-0%;0}")]
    public float SpellSpeed;

    [Tooltip("Spell duration bonus.")] [UIStat("Spell Duration", "{0:+0%;-0%;0}")]
    public float SpellDuration;

    [Tooltip("Cast range bonus (for targeted spells).")] [UIStat("Cast Range", "{0:+0%;-0%;0}")]
    public float CastRange;


    [Header("Offensive Global Bonuses")]
    [Tooltip("Number of projectiles added to all spells.")]
    [UIStat("Amount", "{0:+0;-0;0}")]
    public int Amount;

    [Tooltip("Number of pierces added to all projectiles.")] [UIStat("Pierce", "{0:+0;-0;0}")]
    public int Pierce;

    [Tooltip("Number of bounces added to all projectiles.")] [UIStat("Bounce", "{0:+0;-0;0}")]
    public int Bounce;


    [Header("Critical")] [Tooltip("Critical chance probability (0.0 to 1.0).")] [UIStat("Crit Chance", "{0:+0%;-0%;0}")]
    public float CritChance;

    [Tooltip("Bonus critical damage (0 = normal, 0.5 = +50%).")] [UIStat("Crit Damage", "{0:+0%;-0%;0}")]
    public float CritDamage;
}
