using UnityEngine;

/// <summary>
/// Data definition for a boss. Referenced by a <see cref="BossAuthoring"/> on the boss prefab;
/// its combat values are pushed into the sibling <see cref="EnemyAuthoring"/> via the
/// "Apply Config" button, while identity (name/icon/kind) is baked directly.J'j'
/// </summary>
[CreateAssetMenu(fileName = "NewBoss", menuName = "Survivor/Bosses/Boss")]
public class BossSO : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("Name shown on the boss health bar.")]
    public string DisplayName = "Boss";

    [Tooltip("Optional icon/portrait (unused in v1, reserved for the HUD).")]
    public Sprite Icon;

    [Tooltip("Final boss ends the run on death (2D HUD bar). Elite does not (world-space bar later).")]
    public EBossKind Kind = EBossKind.FinalBoss;

    [Header("Combat (applied to the EnemyAuthoring)")]
    [Tooltip("Core stats copied into the EnemyAuthoring when pressing 'Apply Config'.")]
    public CoreStats BaseStats;

    [Tooltip("Spells the boss casts, copied into the EnemyAuthoring when pressing 'Apply Config'.")]
    public SpellDataSO[] InitialSpells;

    // --- Reserved for later iterations (kept here so the data model does not need a rewrite) ---
    // [Header("Phases")] public float[] PhaseHealthThresholds;       // e.g. 0.66, 0.33
    // [Header("Summons")] public GameObject[] SummonPrefabs;          // necromancer-style adds
    // public float SummonInterval;
}
