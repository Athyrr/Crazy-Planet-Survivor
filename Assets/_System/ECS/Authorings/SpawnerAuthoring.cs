using System.Linq;
using Unity.Entities;
using UnityEngine;

public class SpawnerAuthoring : MonoBehaviour
{
    #region Sub-classes
    
    [System.Serializable]
    public struct SpawnData
    {
        public GameObject Prefab;
        public int Amount;
    }

    [System.Serializable]
    public struct WaveData
    {
        public SpawnData[] SpawnDatas;
    }

    #endregion

    public WaveData[] Waves;
    public float TimeBetweenWaves = 5f;

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

                    waveBuffer.Add(new WaveElement
                    {
                        WaveIndex = i,
                        Prefab = GetEntity(spawnData.Prefab, TransformUsageFlags.Dynamic),
                        Amount = spawnData.Amount
                    });
                }
            }
        }
    }
}