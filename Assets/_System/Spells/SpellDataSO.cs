using UnityEngine;
using UnityEngine.UI;

[CreateAssetMenu(fileName = "NewSpellData", menuName = "Survivor/Spells/Spell Data")]
public class SpellDataSO : ScriptableObject
{
    [Header("General")] [Tooltip("Unique identifier used by the code to recognize this spell logic.")]
    public ESpellID ID;

    [Tooltip("Icon displayed in the UI (Level Up screen, etc.).")]
    public Sprite Icon;

    [Tooltip("Name displayed in the UI (Level Up screen, etc.).")]
    public string DisplayName = string.Empty;

    [TextArea(2, 4)] [Tooltip("Description displayed in the UI.")]
    public string Description;

    [Tooltip(
        "The GameObject prefab that will be baked into an Entity. Must have Authoring components."
    )]
    public GameObject SpellPrefab;

    [Tooltip("The spell rarity used on spell selection.")]
    public int Rarity = 0;


    [Header("Core Combat Stats")] [Tooltip("Tags of the spell (Fire, Ice, etc.) used for resistance calculations.")]
    public ESpellTag Tags;

    [Tooltip("Base damage applied on contact.")]
    public float BaseDamage = 10f;

    [Tooltip("Cooldown time in seconds. Set to 0 or -1 for Passive/Aura spells (cast once).")]
    public float BaseCooldown = 5f;


    [Header("Spatial & Movement")]
    [Tooltip("Movement speed for linear projectiles or rotation speed for orbiting objects.")]
    public float BaseSpeed = 5f;

    [Tooltip(
        "Distance from the caster where the spell spawns (e.g., Orbit Radius or Forward Offset)."
    )]
    public Vector3 BaseSpawnOffset = Vector3.zero;

    [Tooltip("Radius of the area of effect (Explosion radius or Aura size).")]
    public float BaseAreaOfEffect = 1f;

    [Tooltip("Max distance the spell can travel or target (if applicable).")]
    public float BaseCastRange = 5f;

    [Tooltip("Visual size of the spell (affect the scale of the spell).")]
    public float BaseSize = 1f;


    [Header("Targeting")] [Tooltip("Spell targeting mode.")]
    public ESpellTargetingMode TargetingMode = ESpellTargetingMode.CastForward;


    [Header("Lifetime")] [Tooltip("Duration in seconds before the entity destroys itself.")]
    public float Lifetime = 10f;


    [Header("Mechanic: Bouncing")] [Tooltip("Number of times the projectile bounces to a new target after impact.")]
    public int Bounces;

    [Tooltip("Search radius to find the next target when bouncing.")]
    public float BounceRange;


    [Header("Mechanic: Piercing")]
    [Tooltip("Number of enemies the projectile can pass through before being destroyed.")]
    public int Pierces;


    [Header("Tick")] [Tooltip("Time interval in seconds between two damage ticks.")]
    public float TickRate = 1f;


    [Header("Mechanic: Children based Spells (ex: Blades, Perma shield etc..)")]
    [Tooltip("Prefab of the child spell entities spawned by this spell.")]
    public GameObject ChildPrefab;


    [Header("Amount")] [Tooltip("Number of projectile/sub spells.")]
    public int BaseAmount;

    [Tooltip("Radius around the caster where child spells will spawn.")]
    public float ChildrenSpawnRadius = 1f;
}