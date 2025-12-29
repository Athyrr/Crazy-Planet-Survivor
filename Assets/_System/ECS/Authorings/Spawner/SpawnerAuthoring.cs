using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Authoring component for configuring enemy waves and spawner settings.
/// This component is converted into ECS components and buffers during the baking process.
/// </summary>
public class SpawnerAuthoring : MonoBehaviour
{
    #region Data Structures
    
    /// <summary>
    /// Defines a single group of enemies to spawn within a wave.
    /// </summary>
    [System.Serializable]
    public struct SpawnData
    {
        [Tooltip("The spawning strategy for this group.")]
        public SpawnMode Mode;

        [Tooltip("The enemy prefab to spawn.")]
        public GameObject Prefab;
        
        [Tooltip("Number of enemies to spawn.")]
        public int Amount;
        
        [Tooltip("Reference to a GameObject in the scene to use as a spawn point (only for Single mode).")]
        public GameObject SpawnerPrefab;
        
        [Tooltip("Delay in seconds between each spawn in this group.")]
        public float SpawnDelay;
        
        [Tooltip("Minimum distance from the player (only for AroundPlayer mode).")]
        public float MinSpawnRange;
        
        [Tooltip("Maximum distance from the player (only for AroundPlayer mode).")]
        public float MaxSpawnRange;
    }

    /// <summary>
    /// Represents a single wave containing multiple spawn groups.
    /// </summary>
    [System.Serializable]
    public struct WaveData
    {
        public SpawnData[] SpawnDatas;
    }

    #endregion

    [Header("Wave Configuration")]
    [Tooltip("List of waves to be spawned sequentially.")]
    public WaveData[] Waves;
    
    [Tooltip("Time in seconds to wait before the next wave starts.")]
    public float TimeBetweenWaves = 5f;

    #region Debug Visualization
#if UNITY_EDITOR
    [Header("Debug Settings")]
    public bool ShowDebugGizmos = true;
    public Transform DebugPlanetCenter;
    public float DebugPlanetRadius = 50f;
    public Transform DebugPlayer;

    private void OnDrawGizmosSelected()
    {
        if (!ShowDebugGizmos) return;

        Vector3 planetCenter = DebugPlanetCenter != null ? DebugPlanetCenter.position : Vector3.zero;
        float planetRadius = DebugPlanetRadius;
        Vector3 playerPos = DebugPlayer != null ? DebugPlayer.position : Vector3.zero;

        if (Waves == null) return;

        foreach (var wave in Waves)
        {
            if (wave.SpawnDatas == null) continue;

            foreach (var data in wave.SpawnDatas)
            {
                if (data.Mode == SpawnMode.Single && data.SpawnerPrefab != null)
                {
                    Gizmos.color = Color.green;
                    Gizmos.DrawWireSphere(data.SpawnerPrefab.transform.position, 0.5f);
                    Gizmos.DrawLine(planetCenter, data.SpawnerPrefab.transform.position);
                }
                else if (data.Mode == SpawnMode.AroundPlayer && DebugPlayer != null)
                {
                    DrawSpawnRange(planetCenter, playerPos, planetRadius, data.MinSpawnRange, data.MaxSpawnRange);
                }
                else if (data.Mode == SpawnMode.Opposite && DebugPlayer != null)
                {
                    Vector3 dir = (playerPos - planetCenter).normalized;
                    if (dir == Vector3.zero) dir = Vector3.up;
                    Vector3 oppositePos = planetCenter - dir * planetRadius;
                    
                    Gizmos.color = Color.red;
                    Gizmos.DrawWireSphere(oppositePos, 1f);
                }
            }
        }
    }

    private void DrawSpawnRange(Vector3 center, Vector3 playerPos, float radius, float minRange, float maxRange)
    {
        Vector3 up = (playerPos - center).normalized;
        if (up == Vector3.zero) up = Vector3.up;

        // Calculate angles for the arc on the sphere
        float minAngle = Mathf.Clamp(minRange / radius, 0, Mathf.PI);
        float maxAngle = Mathf.Clamp(maxRange / radius, 0, Mathf.PI);

        // Draw inner ring
        UnityEditor.Handles.color = Color.cyan;
        UnityEditor.Handles.DrawWireDisc(center + up * (Mathf.Cos(minAngle) * radius), up, Mathf.Sin(minAngle) * radius);
        
        // Draw outer ring
        UnityEditor.Handles.color = Color.blue;
        UnityEditor.Handles.DrawWireDisc(center + up * (Mathf.Cos(maxAngle) * radius), up, Mathf.Sin(maxAngle) * radius);
    }
#endif
    #endregion

    /// <summary>
    /// Baker class to convert the MonoBehaviour data into ECS components.
    /// </summary>
    private class Baker : Baker<SpawnerAuthoring>
    {
        public override void Bake(SpawnerAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);

            // Add global settings
            AddComponent(entity, new SpawnerSettings
            {
                TimeBetweenWaves = authoring.TimeBetweenWaves
            });

            // Initialize state
            AddComponent(entity, new SpawnerState
            {
                CurrentWaveIndex = 0,
                WaveTimer = 0 
            });

            // Flatten the wave data into a single buffer
            var waveBuffer = AddBuffer<WaveElement>(entity);
            for (int i = 0; i < authoring.Waves.Length; i++)
            {
                var wave = authoring.Waves[i];
                if (wave.SpawnDatas == null) continue;

                foreach (var spawnData in wave.SpawnDatas)
                {
                    if (spawnData.Prefab == null)
                        continue;

                    float3 spawnPos = float3.zero;
                    float spawnDelay = 0f;

                    // Handle specific spawn position for Single mode
                    if (spawnData.Mode == SpawnMode.Single && spawnData.SpawnerPrefab != null)
                    {
                        spawnPos = spawnData.SpawnerPrefab.transform.position;
                        spawnDelay = spawnData.SpawnDelay;
                    }

                    waveBuffer.Add(new WaveElement
                    {
                        WaveIndex = i,
                        Prefab = GetEntity(spawnData.Prefab, TransformUsageFlags.Dynamic),
                        Amount = spawnData.Amount,
                        Mode = spawnData.Mode,
                        SpawnPosition = spawnPos,
                        SpawnDelay = spawnDelay,
                        MinSpawnRange = spawnData.MinSpawnRange,
                        MaxSpawnRange = spawnData.MaxSpawnRange
                    });
                }
            }
        }
    }
}
