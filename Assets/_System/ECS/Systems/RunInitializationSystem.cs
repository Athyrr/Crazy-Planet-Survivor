using Unity.Burst;
using Unity.Entities;

[UpdateInGroup(typeof(InitializationSystemGroup))]
[BurstCompile]
public partial struct RunInitializationSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<StartRunRequest>();
        state.RequireForUpdate<RunProgression>();
        state.RequireForUpdate<GameState>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var ecbSingleton = SystemAPI.GetSingleton<EndInitializationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        foreach (var (req, entity) in SystemAPI.Query<RefRO<StartRunRequest>>().WithEntityAccess())
        {
            // Destroy player and let PlayerSpawner respawn the new one
            if (SystemAPI.TryGetSingletonEntity<Player>(out var playerEntity))
                ecb.DestroyEntity(playerEntity);

            if (SystemAPI.TryGetSingletonRW<RunProgression>(out var runProgression))
            {
                runProgression.ValueRW.Timer = 0;
                runProgression.ValueRW.EnemiesKilledCount = 0;
                runProgression.ValueRW.EnemiesKilledRatio = 0;
                runProgression.ValueRW.ProgressRatio = 0;
            }

            if (SystemAPI.TryGetSingletonRW<SpawnerState>(out var spawnerState))
            {
                spawnerState.ValueRW.CurrentWaveIndex = -1;
                spawnerState.ValueRW.ActiveEnemyCount = 0;
                // Per-wave and per-group runtime are (re)initialized by the spawner when CurrentWaveIndex == -1.
            }

            // Destroy start run request
            ecb.DestroyEntity(entity);
        }
    }
}
