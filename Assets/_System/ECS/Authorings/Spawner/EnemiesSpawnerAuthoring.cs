using Unity.Mathematics;
using Unity.Entities;
using UnityEngine;

/// <summary>
/// Authoring component for configuring enemy waves and spawner settings.
/// </summary>
public class EnemiesSpawnerAuthoring : MonoBehaviour
{
    #region Data Structures

    /// <summary>
    /// Defines a single group of enemies to spawn within a wave.
    /// </summary>
    [System.Serializable]
    public struct SpawnGroupData
    {
        [Tooltip("The spawning strategy for this group.")]
        public SpawnMode Mode;

        [Tooltip("The enemy prefab to spawn.")]
        public GameObject Prefab;

        [Tooltip("Number of enemies to spawn.")]
        public int Amount;

        [Tooltip("Specific position reference (Only used for 'Zone' mode).")]
        public Transform ZoneTransform;

        [Tooltip("Delay in seconds between each spawn in this group.")]
        public float Delay;

        [Tooltip("Minimum distance from the player (Only used for 'AroundPlayer' mode).")]
        public float MinRange;

        [Tooltip("Maximum distance from the player (Only used for 'AroundPlayer' mode).")]
        public float MaxRange;

        [Tooltip("Uniform scale of the spawned entities. 0 = default (1). Use >1 for bosses.")]
        public float Scale;
    }

    /// <summary>
    /// Represents a single wave containing multiple spawn groups.
    /// </summary>
    [System.Serializable]
    public struct WaveData
    {
        [Tooltip("Name of the wave for editor identification.")]
        public string Name;

        [Tooltip("Maximum duration of the wave in seconds before force-starting the next one.")]
        public float Duration;

        [Tooltip("Percentage of total wave enemies (0-1) that must be killed to trigger the next wave early. " +
                 "Set to 0 to disable the kill condition (advance on Duration only).")]
        [Range(0f, 1f)]
        public float KillPercentageToAdvance;

        [Tooltip("If enabled, after its first run this wave keeps repeating its groups over and over, in " +
                 "parallel with the following waves. Ignored (and stripped) for a wave that contains a final boss.")]
        public bool Loop;

        [Tooltip("If enabled, this wave's enemies spawn around the final boss (a ring using each group's " +
                 "Min/MaxRange) instead of their group's normal mode. Must be placed after the boss wave; the " +
                 "wave waits until a final boss exists.")]
        public bool AroundBoss;

        [Tooltip("List of enemy groups to spawn in this wave.")]
        public SpawnGroupData[] Groups;
    }

    #endregion


    [Header("Global Settings")]
    [Tooltip("Maximum number of enemies allowed in the game at once.")]
    public int MaxEnemies = 500;

    [Header("Wave Configuration")]
    [Tooltip("List of waves to be spawned sequentially.")]
    public WaveData[] Waves = new WaveData[0];


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

        if (Waves.Length == 0) 
            return;

        foreach (var wave in Waves)
        {
            if (wave.Groups == null) continue;

            foreach (var groupData in wave.Groups)
            {
                // Visualizing Zone Spawns
                if (groupData.Mode == SpawnMode.Zone && groupData.ZoneTransform != null)
                {
                    Gizmos.color = Color.green;
                    Gizmos.DrawWireSphere(groupData.ZoneTransform.position, 0.5f);
                    Gizmos.DrawLine(planetCenter, groupData.ZoneTransform.position);
                }
                // Visualizing Around Player Range
                else if (groupData.Mode == SpawnMode.AroundPlayer && DebugPlayer != null)
                {
                    DrawSpawnRange(planetCenter, playerPos, planetRadius, groupData.MinRange, groupData.MaxRange);
                }
                // Visualizing Opposite Player Point
                else if (groupData.Mode == SpawnMode.PlayerOpposite && DebugPlayer != null)
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

        // Calculate angles for the arc on the sphere (approximation for visualization)
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
    /// Baker class responsible for converting the authoring data into ECS components.
    /// </summary>
    class Baker : Baker<EnemiesSpawnerAuthoring>
    {
        public override void Bake(EnemiesSpawnerAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);

            // Global Settings
            AddComponent(entity, new SpawnerSettings { MaxEnemies = authoring.MaxEnemies });

            // Spawn State
            AddComponent(entity, new SpawnerState
            {
                CurrentWaveIndex = -1 // -1 indicates the system needs to initialize the first wave
            });

            // 3. Buffers
            var waveBuffer = AddBuffer<Wave>(entity);
            // Runtime state, kept index-aligned 1:1 with waveBuffer (armed per wave at runtime).
            var waveRuntimeBuffer = AddBuffer<WaveRuntime>(entity);
            var groupBuffer = AddBuffer<SpawnGroup>(entity);
            // Runtime state, kept index-aligned 1:1 with groupBuffer (filled per wave at runtime).
            var groupRuntimeBuffer = AddBuffer<SpawnGroupRuntime>(entity);

            int currentGroupStartIndex = 0;

            // Fill Groups Buffer
            foreach (var waveData in authoring.Waves)
            {
                // Count only the groups actually added (null-prefab groups are skipped), so GroupStartIndex
                // and GroupCount stay aligned with the buffers the runtime indexes into.
                int groupsAddedInThisWave = 0;
                int totalEnemiesInWave = 0;
                bool waveContainsFinalBoss = false;

                if (waveData.Groups != null)
                {
                    foreach (var groupData in waveData.Groups)
                    {
                        if (groupData.Prefab == null)
                            continue;

                        totalEnemiesInWave += groupData.Amount;

                        if (ContainsFinalBoss(groupData.Prefab))
                            waveContainsFinalBoss = true;

                        groupBuffer.Add(new SpawnGroup
                        {
                            Prefab = GetEntity(groupData.Prefab, TransformUsageFlags.Dynamic),
                            Amount = groupData.Amount,
                            Mode = groupData.Mode,
                            Position = groupData.ZoneTransform ? groupData.ZoneTransform.position : float3.zero,
                            SpawnDelay = groupData.Delay,
                            MinRange = groupData.MinRange,
                            MaxRange = groupData.MaxRange,
                            Scale = groupData.Scale
                        });

                        // Mirror entry; activated (Remaining set) when its wave starts.
                        groupRuntimeBuffer.Add(new SpawnGroupRuntime { Remaining = 0, SpawnTimer = 0f });

                        groupsAddedInThisWave++;
                    }
                }

                // A looping wave containing the final boss would respawn it after death and emit several
                // EndRunRequest{Success} (RunManager.CheckEndRun uses GetSingletonEntity -> would throw).
                bool loop = waveData.Loop;
                if (loop && waveContainsFinalBoss)
                {
                    UnityEngine.Debug.LogWarning(
                        $"[EnemiesSpawnerAuthoring] Wave '{waveData.Name}' is marked Loop but contains a final " +
                        "boss; disabling Loop for it (a looping boss would win the run repeatedly).");
                    loop = false;
                }

                // Fill Waves Buffer
                waveBuffer.Add(new Wave
                {
                    Duration = waveData.Duration,
                    KillPercentage = waveData.KillPercentageToAdvance,
                    GroupStartIndex = currentGroupStartIndex,
                    GroupCount = groupsAddedInThisWave,
                    TotalEnemyCount = totalEnemiesInWave,
                    Loop = loop,
                    AroundBoss = waveData.AroundBoss
                });

                // Mirror entry; armed (Active/Timer set) when its wave starts.
                waveRuntimeBuffer.Add(new WaveRuntime { Active = false, Timer = 0f, KilledCount = 0 });

                // Advance the start index for the next wave
                currentGroupStartIndex += groupsAddedInThisWave;
            }
        }

        /// <summary>
        /// True if the prefab carries a <see cref="BossAuthoring"/> configured as a final boss (or with no
        /// config, which BossAuthoring itself defaults to FinalBoss). Used to forbid looping a boss wave.
        /// </summary>
        private bool ContainsFinalBoss(GameObject prefab)
        {
            var boss = GetComponent<BossAuthoring>(prefab);
            if (boss == null)
                return false;

            return boss.Config == null || boss.Config.Kind == EBossKind.FinalBoss;
        }
    }
}