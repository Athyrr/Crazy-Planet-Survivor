using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[BurstCompile]
public partial struct PlayerSpawnerSystem : ISystem
{
    //[BurstCompile]
    //public void OnCreate(ref SystemState state)
    //{
    //    state.RequireForUpdate<GameState>();
    //    state.RequireForUpdate<PlanetData>();
    //    state.RequireForUpdate<PlayerStart>();
    //}

    //[BurstCompile]
    //public void OnUpdate(ref SystemState state)
    //{
    //    if (!SystemAPI.QueryBuilder().WithAll<Player>().Build().IsEmpty)
    //        return;

    //    // @todo SelectedCharacterData pour stocker le dernier perso joué une fois retorurné au lobby.
    //    if (!SystemAPI.TryGetSingleton<SelectedCharacterData>(out var selection))
    //        return;

    //    if (SystemAPI.TryGetSingleton<GameState>(out var gameState))
    //    {
    //        if (gameState.State == EGameState.Lobby || gameState.State == EGameState.Running)
    //        {
    //            var playerEntity = state.EntityManager.Instantiate(selection.CharacterPrefab);

    //            var pos = (gameState.State == EGameState.Lobby)
    //                      ? new float3(0, 51, 0)
    //                      : new float3(0, 51, 0);

    //            state.EntityManager.SetComponent(playerEntity, new LocalTransform
    //            {
    //                Position = pos,
    //                Rotation = quaternion.identity,
    //                Scale = 1
    //            });
    //        }
    //    }
    //}
}
