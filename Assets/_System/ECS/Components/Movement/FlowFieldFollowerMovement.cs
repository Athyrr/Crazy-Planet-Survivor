using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Opts an entity into flow-field-based navigation, and carries its locomotion state.
/// Entities with this component ignore the direct-to-target FollowTargetMovement
/// and instead sample the FlowFieldData singleton to determine their movement direction.
/// Requires HardSnappedMovement to also be present for terrain snapping.
///
/// <see cref="Velocity"/> is what makes the motion readable: without persistent velocity the heading is
/// recomputed from scratch every frame, and the avoidance term reverses sign tick to tick in a packed
/// crowd — so the direction jitters and anything oriented from it spins on the spot. Ramping a stored
/// velocity toward the desired one low-passes that noise (the standard Reynolds steering model).
/// </summary>
public struct FlowFieldFollowerMovement : IComponentData, IEnableableComponent
{
    /// <summary> Persistent world-space velocity, kept tangent to the planet surface. Runtime state. </summary>
    public float3 Velocity;

    /// <summary> Acceleration in units/s^2. &lt;= 0 inherits the global CpBaseEnemySettings default. </summary>
    public float Acceleration;

    /// <summary> Maximum turn rate in degrees/second. &lt;= 0 inherits the global default. </summary>
    public float MaxTurnRateDeg;
}
