/// <summary>
/// Blittable snapshot of the GLOBAL avoidance tuning, resolved once per frame from the managed
/// CpAvoidanceSettings and handed to the Burst jobs. Per-entity radius/mass live on the Avoidance
/// component; only shared parameters (grid cell sizes, force gain, obstacle mass...) live here.
///
/// Grid routing is by radius: an entity whose radius fits the fine grid stays there (cheap, numerous
/// small entities); anything larger — and every obstacle — goes to the coarse grid.
/// </summary>
public struct AvoidanceConfig
{
    /// <summary> Cell size of the fine spatial-hash grid. </summary>
    public float FineCellSize;

    /// <summary> Cell size of the coarse grid (large entities + obstacles). </summary>
    public float CoarseCellSize;

    /// <summary> Radius threshold: entities with Radius above this route to the coarse grid. </summary>
    public float FineGridMaxRadius;

    /// <summary> Mass assigned to static obstacles: large enough that the mass ratio makes them unmovable. </summary>
    public float ObstacleMass;

    /// <summary> Penetration-force gain (the old hard-coded "* 5"). </summary>
    public float ForceGain;

    /// <summary> Clamp on the summed steering force magnitude. </summary>
    public float MaxSteeringForce;

    /// <summary> True if an entity of this radius belongs to the coarse grid. </summary>
    public bool IsCoarse(float radius) => radius > FineGridMaxRadius;

    public float CellSizeFor(float radius) => IsCoarse(radius) ? CoarseCellSize : FineCellSize;

    /// <summary> Safe built-in defaults used when no CpAvoidanceSettings asset is present. </summary>
    public static AvoidanceConfig Default => new AvoidanceConfig
    {
        FineGridMaxRadius = 5f,
        FineCellSize = 11f,     // ~ 2 * FineGridMaxRadius * safety
        CoarseCellSize = 64f,
        ObstacleMass = 1e6f,
        ForceGain = 5f,
        MaxSteeringForce = 5f
    };
}
