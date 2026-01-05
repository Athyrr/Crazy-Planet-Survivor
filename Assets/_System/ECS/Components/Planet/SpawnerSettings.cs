using Unity.Entities;
using Unity.Mathematics;


public enum SpawnMode { EntirePlanet, Single, Opposite, AroundPlayer }

public struct SpawnerSettings : IComponentData
{
    public float TimeBetweenWaves;
}

public struct WaveElement : IBufferElementData
{
    public int WaveIndex;
    public Entity Prefab;
    public int Amount;
    public SpawnMode Mode;
    public float3 SpawnPosition;
    public float SpawnDelay;
    public float MinSpawnRange;
    public float MaxSpawnRange;
}


public struct SpawnerState : IComponentData
{
    public int CurrentWaveIndex;
    public float WaveTimer;
    
    // Fields for multi-frame spawning
    public int PendingSpawnCount;
    public int SpawnsProcessed;
    public int CurrentWaveElementIndex;
}