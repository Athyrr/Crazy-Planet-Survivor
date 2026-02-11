using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;

[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial struct PlayerInitializerSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameState>();
        state.RequireForUpdate<PlanetData>();
        state.RequireForUpdate<PlayerStart>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        state.Enabled = false;

        if (!SystemAPI.TryGetSingleton<GameState>(out var gameState))
            return;

        if (gameState.State != EGameState.Lobby && gameState.State != EGameState.Running)
            return;

        if (!SystemAPI.TryGetSingletonEntity<PlayerStart>(out var playerStart))
            return;

        var startPosition = SystemAPI.GetComponent<LocalTransform>(playerStart);


    }
}
