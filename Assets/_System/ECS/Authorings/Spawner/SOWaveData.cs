using System;
using UnityEngine;

namespace _System.ECS.Authorings.Spawner
{
    public class SOWaveData: ScriptableObject
    {
        /// <summary>
        /// Represents a single wave containing multiple spawn groups.
        /// </summary>
        [Serializable]
        public struct WaveData
        {
            [Tooltip("Name of the wave for editor identification.")]
            public string Name;

            [Tooltip("Maximum duration of the wave in seconds before force-starting the next one.")]
            public float Duration;

            [Tooltip("Percentage of total wave enemies (0-1) that must be killed to trigger the next wave early.")]
            [Range(0f, 1f)]
            public float KillPercentageToAdvance;

            [Tooltip("List of enemy groups to spawn in this wave.")]
            public EnemiesSpawnerAuthoring.SpawnGroupData[] Groups;
        }

        [SerializeField] public WaveData[] Waves = new WaveData[0];
    }
}