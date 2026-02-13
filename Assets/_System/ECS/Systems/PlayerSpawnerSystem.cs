using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[BurstCompile]
public partial struct PlayerSpawnerSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameState>();
        state.RequireForUpdate<CharactersDatabase>();
        state.RequireForUpdate<SelectedCharacterData>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.QueryBuilder().WithAll<Player>().Build().IsEmpty)
            return;

        if (!SystemAPI.TryGetSingleton<GameState>(out var gameState)) 
            return;
        if (gameState.State != EGameState.Lobby && gameState.State != EGameState.Running) 
            return;

        if (!SystemAPI.TryGetSingleton<SelectedCharacterData>(out SelectedCharacterData selection)) 
            return;

        var databaseEntity = SystemAPI.GetSingletonEntity<CharactersDatabase>();
        var characterPrefabsBuffer = SystemAPI.GetBuffer<CharacterPrefabBufferElement>(databaseEntity);

        if (selection.CharacterIndex < 0 || selection.CharacterIndex >= characterPrefabsBuffer.Length) 
            return;

        var prefabToSpawn = characterPrefabsBuffer[selection.CharacterIndex].CharacterPrefabEntity;
        if (prefabToSpawn == Entity.Null) 
            return;

        float3 position = float3.zero;
        quaternion rot = quaternion.identity;

        if (SystemAPI.TryGetSingletonEntity<ForceSpawnPosition>(out var forceEntity))
        {
            var force = SystemAPI.GetComponent<ForceSpawnPosition>(forceEntity);
            position = force.Position;
            rot = force.Rotation;

            state.EntityManager.DestroyEntity(forceEntity);
        }
        else if (SystemAPI.TryGetSingleton<PlayerStart>(out PlayerStart startPoint))
        {
            position = startPoint.Position;
            rot = startPoint.Rotation;
        }
        else
        {
            position = new float3(0f, 51f, 0); 
        }

        var playerEntity = state.EntityManager.Instantiate(prefabToSpawn);
        state.EntityManager.SetComponentData(playerEntity, new LocalTransform
        {
            Position = position,
            Rotation = rot,
            Scale = 1
        });
    }
}