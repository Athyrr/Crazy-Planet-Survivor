using Unity.Entities;

/// <summary>
/// Tag component indicating that an entity has avoidance behavior.
/// </summary>
public struct  Avoidance : IComponentData
{
    /// <summary>
    /// Detection radius for avoidance behavior.
    /// </summary>
    public float Radius;

    /// <summary>
    /// Avoidance weight factor.
    /// </summary>
    public float Weight;
}
