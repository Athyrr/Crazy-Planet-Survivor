using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Geometry of the streamed planet (one per planet scene). Identity is not here: the loader writes it in
/// CurrentPlanet; the run duration is global (PlanetsDatabase.RunDuration).
/// </summary>
public struct PlanetData : IComponentData
{
    public Entity Prefab;

    public float3 Center;

    public float Radius;
}
