using Unity.Entities;

/// <summary>
/// Marks an entity as a boss. Could be elite nme or final boss
/// </summary>
public struct Boss : IComponentData
{
    public EBossKind Kind;
}

/// <summary>
/// Distinguishes a planet's final boss (defeating it ends the run in victory, shown in the 2D HUD)
/// from in-run mini-bosses (do not end the run, displayed with a world-space bar later).
/// </summary>
public enum EBossKind
{
    MiniBoss,
    FinalBoss
}

/// <summary>
/// Tag added only to a <see cref="EBossKind.FinalBoss"/>.
/// </summary>
public struct FinalBossTag : IComponentData
{
}
