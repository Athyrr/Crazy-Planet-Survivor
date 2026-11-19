using UnityEngine;

/// <summary>
/// Definition of a boss or an elite: an <see cref="EnemySO"/> (combat values, identity, optional parent, which can
/// be an EnemySO or another BossSO) plus its <see cref="Kind"/>. Referenced by the boss prefab's
/// <see cref="EnemyAuthoring.Config"/>; <see cref="BossAuthoring"/> bakes Kind, DisplayName and Icon from the
/// resolved definition.
/// </summary>
[CreateAssetMenu(fileName = "NewBoss", menuName = "Survivor/Bosses/Boss")]
public class BossSO : EnemySO
{
    [Header("Boss")]
    [Tooltip("Final boss ends the run on death (2D HUD bar). Elite does not (world-space bar later).")]
    public EBossKind Kind = EBossKind.FinalBoss;

    // --- Reserved for later iterations (kept here so the data model does not need a rewrite) ---
    // [Header("Phases")] public float[] PhaseHealthThresholds;       // e.g. 0.66, 0.33
    // [Header("Summons")] public GameObject[] SummonPrefabs;          // necromancer-style adds
    // public float SummonInterval;
}
