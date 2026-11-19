using UnityEngine;

/// <summary>
/// Values of an enemy, referenced by <see cref="EnemyAuthoring.Config"/> (the prefab keeps the structure). Can
/// inherit from a parent EnemySO (or BossSO): each field, and each CoreStats stat, either overrides the parent value
/// or inherits it. Resolved at bake time only, never read as-is at runtime.
/// </summary>
[CreateAssetMenu(fileName = "NewEnemyData", menuName = "Survivor/Enemies/Enemy")]
public class EnemySO : InheritableDefinitionSO<EnemySO>
{
    [Header("Movement precision")]
    [Tooltip("If true, the enemy will be snapped perfectly on the ground following the terrain height. Otherwise, it will follow the base radius.")]
    public bool UseSnappedMovement = true;

    [Header("Movement feel")]
    [Tooltip("Acceleration in units/s². Leave at 0 to inherit CpBaseEnemySettings. Raise for darting " +
             "enemies, lower for heavy ones that take time to get going.")]
    public float Acceleration = 0f;

    [Tooltip("Max turn rate in degrees/second. Leave at 0 to inherit CpBaseEnemySettings. Lower it on " +
             "big enemies so they describe wide curves instead of pivoting on the spot.")]
    public float MaxTurnRateDeg = 0f;

    [Header("Stats")]
    public CoreStats BaseStats;

    [Header("Spells")]
    public SpellSO[] InitialSpells;

    [Header("Contact damage")]
    [Tooltip("Damage dealt to whatever Destructible this enemy's body touches (the player).")]
    public float ContactDamage = 10f;
}
