using Unity.Entities;


public struct SpawnerSettings : IComponentData
{
    public float TimeBetweenWaves;
}

public struct WaveElement : IBufferElementData
{
    public int WaveIndex;
    public Entity Prefab;
    public int Amount;
}


public struct SpawnerState : IComponentData
{
    public int CurrentWaveIndex;
    public float WaveTimer;
}