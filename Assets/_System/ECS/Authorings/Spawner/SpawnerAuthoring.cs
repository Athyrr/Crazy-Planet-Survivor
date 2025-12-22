using System.Diagnostics;
using System.Linq;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.Tilemaps;

public class SpawnerAuthoring : MonoBehaviour
{
    #region Sub-classes
    
    [System.Serializable]
    public struct SpawnData
    {
        public SpawnMode Mode;

        public GameObject Prefab;
        public int Amount;
        public GameObject SpawnerPrefab;
        public float SpawnDelay;
        public float MinSpawnRange;
        public float MaxSpawnRange;
    }

    [System.Serializable]
    public struct WaveData
    {
        public SpawnData[] SpawnDatas;
    }

    #endregion

    public WaveData[] Waves;
    public float TimeBetweenWaves = 5f;

    #region Debug
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

        float minAngle = Mathf.Clamp(minRange / radius, 0, Mathf.PI);
        float maxAngle = Mathf.Clamp(maxRange / radius, 0, Mathf.PI);

        UnityEditor.Handles.color = Color.cyan;
        UnityEditor.Handles.DrawWireDisc(center + up * (Mathf.Cos(minAngle) * radius), up, Mathf.Sin(minAngle) * radius);
        
        UnityEditor.Handles.color = Color.blue;
        UnityEditor.Handles.DrawWireDisc(center + up * (Mathf.Cos(maxAngle) * radius), up, Mathf.Sin(maxAngle) * radius);
    }
#endif
    #endregion

    private class Baker : Baker<SpawnerAuthoring>
    {
        public override void Bake(SpawnerAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);

            AddComponent(entity, new SpawnerSettings
            {
                TimeBetweenWaves = authoring.TimeBetweenWaves
            });

            AddComponent(entity, new SpawnerState
            {
                CurrentWaveIndex = 0,
                WaveTimer = 0 
            });

            var waveBuffer = AddBuffer<WaveElement>(entity);
            for (int i = 0; i < authoring.Waves.Length; i++)
            {
                var wave = authoring.Waves[i];
                foreach (var spawnData in wave.SpawnDatas)
                {
                    if (spawnData.Prefab == null)
                        continue;

                    float3 spawnPos = float3.zero;
                    float spawnDelay = 0f;

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