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
    /// <summary> Percentage of enemies (0-1) that must be killed to trigger next wave early. </summary>
    public float KillPercentageToAdvance;
}


public struct SpawnerState : IComponentData
{
    public int CurrentWaveIndex;
    public float WaveTimer;
    
    // Fields for multi-frame spawning
    public int PendingSpawnCount;
    public int SpawnsProcessed;
    public int CurrentWaveElementIndex;
    
    // Fields for tracking wave progress
    public int TotalEnemiesInCurrentWave;
    public int EnemiesKilledInCurrentWave;
    
    /// <summary> Current number of active enemies in the game. </summary>
    public int CurrentEnemyCount;
}