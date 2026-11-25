using Unity.Entities;

/// <summary>
/// Singleton of the baked planets database, on the entity that carries the PlanetEntry buffer: the role indices and
/// the global run duration.
/// </summary>
public struct PlanetsDatabase : IComponentData
{
    public int LobbyIndex;
    public int SandboxIndex;

    /// <summary>Run duration in seconds, the same on every planet (PlanetsDatabaseSO.RunDuration).</summary>
    public float RunDuration;

    /// <summary>
    /// Main-thread check for managed code: a planet scene is streamed (one PlanetData) and the loader's planet is the
    /// Sandbox. Replaces the former PlanetData.PlanetID == Sandbox test.
    /// </summary>
    public static bool IsCurrentSandbox(EntityManager em)
    {
        using var planetQuery = em.CreateEntityQuery(typeof(PlanetData));
        using var currentQuery = em.CreateEntityQuery(typeof(CurrentPlanet));
        using var databaseQuery = em.CreateEntityQuery(typeof(PlanetsDatabase));
        return planetQuery.CalculateEntityCount() == 1
               && currentQuery.TryGetSingleton(out CurrentPlanet current)
               && databaseQuery.TryGetSingleton(out PlanetsDatabase database)
               && current.Index != CurrentPlanet.None
               && current.Index == database.SandboxIndex;
    }
}
