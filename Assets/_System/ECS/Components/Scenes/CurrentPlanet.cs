using Unity.Entities;

/// <summary>
/// Planet the loader shows: its index in the baked planets database. Created by SceneLoadingSystem.OnCreate,
/// written when a load starts (index of the request) and reset to <see cref="None"/> when a load fails. Persistent:
/// no RunScope, so ClearRunRequest never destroys it.
/// </summary>
public struct CurrentPlanet : IComponentData
{
    public const int None = -1;

    public int Index;

    /// <summary>This planet's entry in the baked database; false when no planet is loaded or the index is out of range.</summary>
    public bool TryGetEntry(DynamicBuffer<PlanetEntry> entries, out PlanetEntry entry)
    {
        bool valid = Index >= 0 && Index < entries.Length;
        entry = valid ? entries[Index] : default;
        return valid;
    }
}
