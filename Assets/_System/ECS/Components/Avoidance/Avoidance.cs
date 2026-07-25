using Unity.Entities;

/// <summary>
/// Marks an entity as participating in local avoidance, and carries its per-entity avoidance
/// parameters. Radius and Mass are authored per prefab and are fully independent: two entities of the
/// same visual size can have different masses, and a small entity can be heavier than a big one.
/// Enableable: AvoidanceSystem's LOD toggles it off past the activation radius.
/// </summary>
public struct Avoidance : IComponentData, IEnableableComponent
{
    /// <summary> Avoidance footprint / detection radius (personal space), in world units. </summary>
    public float Radius;

    /// <summary>
    /// Mass. Repulsion is split by mass ratio, so a heavier entity shoves lighter ones aside while
    /// barely being moved itself. A very high mass approaches "immovable" (like an obstacle).
    /// </summary>
    public float Mass;
}
