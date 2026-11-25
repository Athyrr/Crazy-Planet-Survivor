using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The planets database: every planet the game can load, by role. A planet's index in this database is its only
/// runtime key (LoadSceneRequest.PlanetIndex, CurrentPlanet.Index): its position in [Lobby, Sandbox, Planets...],
/// computed by <see cref="Get"/> / <see cref="IndexOf"/> for the baker (PlanetsDatabaseAuthoring) and the managed side
/// (GameManager) alike. The index is never written by hand nor serialized: a persisted planet must use a stable key
/// (asset GUID or name), never the index.
/// </summary>
[CreateAssetMenu(fileName = "GamePlanetsDatabase", menuName = "Survivor/Databases/Planets")]
public class PlanetsDatabaseSO : ScriptableObject
{
    public const int LobbyIndex = 0;
    public const int SandboxIndex = 1;
    private const int FirstPlayableIndex = 2;

    [Tooltip("Planet loaded by New Game and Return To Lobby.")]
    public PlanetSO Lobby;

    [Tooltip("Planet loaded by the `sandbox` console command only (no menu entry).")]
    public PlanetSO Sandbox;

    [Tooltip("Playable planets, selectable in the lobby.")]
    public PlanetSO[] Planets = new PlanetSO[0];

    [Header("Run")]
    [Tooltip("Run duration in seconds, the same on every planet. Baked with the database; RunProgressionSystem " +
             "derives RunProgression.ProgressRatio from it.")]
    [Min(0f)]
    public float RunDuration = 600f;

    /// <summary>Number of indices: Lobby, Sandbox, then every Planets slot (empty slots included).</summary>
    public int Count => FirstPlayableIndex + (Planets != null ? Planets.Length : 0);

    /// <summary>Planet at a database index; null when the index is out of range or the slot is empty.</summary>
    public PlanetSO Get(int index)
    {
        if (index == LobbyIndex) return Lobby;
        if (index == SandboxIndex) return Sandbox;
        int i = index - FirstPlayableIndex;
        return Planets != null && i >= 0 && i < Planets.Length ? Planets[i] : null;
    }

    /// <summary>Database index of a planet; -1 when the planet is null or not in the database.</summary>
    public int IndexOf(PlanetSO planet)
    {
        if (planet == null) return -1;
        for (int i = 0; i < Count; i++)
            if (Get(i) == planet)
                return i;
        return -1;
    }

    /// <summary>
    /// Errors that make the indices unreliable: empty Lobby / Sandbox slot, empty Planets slot, a planet listed twice,
    /// a planet without a valid scene. PlanetsDatabaseAuthoring refuses the whole database when any is found.
    /// </summary>
    public void CollectErrors(List<string> errors)
    {
        if (Lobby == null) errors.Add("Lobby is empty.");
        if (Sandbox == null) errors.Add("Sandbox is empty.");
        var seen = new Dictionary<PlanetSO, int>();
        for (int i = 0; i < Count; i++)
        {
            PlanetSO planet = Get(i);
            if (planet == null)
            {
                if (i >= FirstPlayableIndex) errors.Add($"Planets[{i - FirstPlayableIndex}] is empty.");
                continue;
            }
            if (seen.TryGetValue(planet, out int first))
                errors.Add($"{planet.name} is listed twice (indices {first} and {i}).");
            else
                seen.Add(planet, i);
            if (!planet.Scene.IsReferenceValid)
                errors.Add($"{planet.name} has no valid Scene.");
        }
    }

#if UNITY_EDITOR
    /// <summary>Editor only: PlanetSO assets of the project that are not in this database (reported, not blocking).</summary>
    public void CollectUnlisted(List<string> errors)
    {
        foreach (PlanetSO planet in DatabaseAutoPopulateUtils.FindAllAssets<PlanetSO>())
            if (IndexOf(planet) < 0)
                errors.Add($"{planet.name} is not in the planets database.");
    }

    [EasyButtons.Button("Populate (find all Planets)")]
    public void Populate()
    {
        Planets = DatabaseAutoPopulateUtils.FindAllAssets<PlanetSO>(p => p != Lobby && p != Sandbox);
        DatabaseAutoPopulateUtils.Save(this);
        Debug.Log($"[{name}] Populated {Planets.Length} playable planets.", this);
    }

    [EasyButtons.Button("Validate")]
    public void Validate()
    {
        var errors = new List<string>();
        CollectErrors(errors);
        CollectUnlisted(errors);
        foreach (string error in errors)
            Debug.LogError($"[{name}] {error}", this);
        if (errors.Count == 0)
            Debug.Log($"[{name}] {Count} planets, no error.", this);
    }
#endif
}
