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
/// from in-run elites (do not end the run, displayed with a world-space bar later).
/// </summary>
public enum EBossKind
{
    Elite,
    FinalBoss
}

/// <summary>
/// Tag added only to a <see cref="EBossKind.FinalBoss"/>.
/// </summary>
public struct FinalBossTag : IComponentData
{
}

/// <summary>
/// Tag added only to an <see cref="EBossKind.Elite"/> (in-run mini-boss shown with a world-space health bar).
/// Unlike <see cref="FinalBossTag"/>, defeating an elite does not end the run.
/// </summary>
public struct EliteTag : IComponentData
{
}
