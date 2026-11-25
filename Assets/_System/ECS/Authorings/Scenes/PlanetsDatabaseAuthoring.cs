using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

/// <summary>
/// Bakes the planets database: one PlanetEntry per planet in database index order ([Lobby, Sandbox, Planets...],
/// PlanetsDatabaseSO.Get) and the PlanetsDatabase singleton. Any structural error (PlanetsDatabaseSO.CollectErrors)
/// refuses the whole database, so a baked index can never point at another planet than the managed side's.
/// </summary>
public class PlanetsDatabaseAuthoring : MonoBehaviour
{
    [Tooltip("Planets database baked into SC_Entity_Core. Must be the asset GameManager references.")]
    public PlanetsDatabaseSO Database;

    private class Baker : Baker<PlanetsDatabaseAuthoring>
    {
        public override void Bake(PlanetsDatabaseAuthoring authoring)
        {
            PlanetsDatabaseSO database = authoring.Database;
            if (database == null)
            {
                Debug.LogError("[PlanetsDatabaseAuthoring] Database is missing: no planet can be loaded.", authoring);
                return;
            }

            // The database and every planet read below are dependencies: editing a planet's scene or camera, or the
            // database itself, must rebake this subscene even when it is closed.
            DependsOn(database);
            for (int i = 0; i < database.Count; i++)
            {
                PlanetSO planet = database.Get(i);
                if (planet != null)
                    DependsOn(planet);
            }

            var errors = new List<string>();
            database.CollectErrors(errors);
            if (errors.Count > 0)
            {
                foreach (string error in errors)
                    Debug.LogError($"[PlanetsDatabaseAuthoring] {database.name}: {error} The planets database is not baked.", database);
                return;
            }

            var entity = GetEntity(TransformUsageFlags.None);
            AddComponent(entity, new PlanetsDatabase
            {
                LobbyIndex = PlanetsDatabaseSO.LobbyIndex,
                SandboxIndex = PlanetsDatabaseSO.SandboxIndex,
                RunDuration = database.RunDuration,
            });

            var entries = AddBuffer<PlanetEntry>(entity);
            for (int i = 0; i < database.Count; i++)
            {
                PlanetSO planet = database.Get(i);
                entries.Add(new PlanetEntry
                {
                    Scene = planet.Scene,
                    OverrideCamera = planet.OverrideCamera,
                    Camera = planet.Camera,
                });
            }
        }
    }
}
