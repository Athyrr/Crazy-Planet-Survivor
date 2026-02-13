using Unity.Transforms;
using Unity.Entities;
using Unity.Burst;
using Unity.Mathematics;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[BurstCompile]
public partial struct CharacterSelectionSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<CharactersDatabase>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton<GameState>(out var gameState))
            return;

        if (gameState.State != EGameState.Lobby)
            return;

        if (!SystemAPI.TryGetSingletonEntity<SelectCharacterRequest>(out var selectCharacterRequestEntity))
            return;

        if (!SystemAPI.TryGetSingletonEntity<SelectedCharacterData>(out var selectedCharacterDataEntity))
            return;

        var selectedCharacterData = SystemAPI.GetComponent<SelectedCharacterData>(selectedCharacterDataEntity);

        var request = SystemAPI.GetComponent<SelectCharacterRequest>(selectCharacterRequestEntity);

        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        var databaseEntity = SystemAPI.GetSingletonEntity<CharactersDatabase>();
        var charactersBuffer = SystemAPI.GetBuffer<CharacterPrefabBufferElement>(databaseEntity);

        //if (request.CharacterIndex >= 0 && request.CharacterIndex < charactersBuffer.Length)
        //{
        //    var selectedCharacterPrefab = charactersBuffer[request.CharacterIndex].CharacterPrefabEntity;

        //    if (SystemAPI.TryGetSingletonEntity<Player>(out var currentPlayer))
        //    {
        //        // Store current player position
        //        var currentPlayerTransform = SystemAPI.GetComponent<LocalTransform>(currentPlayer);

        //        // Destroy current player
        //        ecb.DestroyEntity(currentPlayer);

        //        // Instantiate selected character
        //        var selectedPlayer = ecb.Instantiate(selectedCharacterPrefab);

        //        // Set position
        //        ecb.SetComponent(selectedPlayer, currentPlayerTransform);
        //    }
        //    // If no player yet
        //    else
        //    {
        //        var newPlayer = ecb.Instantiate(selectedCharacterPrefab);
        //        ecb.SetComponent(newPlayer, LocalTransform.FromPosition(0, 50.5f, 0)); // auto scale to 1
        //    }

        //    ecb.SetComponent(selectedCharacterDataEntity, new SelectedCharacterData
        //    {
        //        CharacterIndex = request.CharacterIndex,
        //        CharacterPrefab = selectedCharacterPrefab
        //    });
        //}

        if (request.CharacterIndex >= 0 && request.CharacterIndex < charactersBuffer.Length)
        {
            var selectedPrefab = charactersBuffer[request.CharacterIndex].CharacterPrefabEntity;
            var selectedDataEntity = SystemAPI.GetSingletonEntity<SelectedCharacterData>();

            SystemAPI.SetComponent(selectedDataEntity, new SelectedCharacterData
            {
                CharacterIndex = request.CharacterIndex,
            });

            if (SystemAPI.TryGetSingletonEntity<Player>(out var currentPlayer))
            {
                var transform = SystemAPI.GetComponent<LocalTransform>(currentPlayer);


                var forcePositionEntity = state.EntityManager.CreateEntity();
                state.EntityManager.AddComponentData(forcePositionEntity, new ForceSpawnPosition
                {
                    Position = transform.Position,
                    Rotation = transform.Rotation
                });

                state.EntityManager.DestroyEntity(currentPlayer);
            }
        }

        ecb.DestroyEntity(selectCharacterRequestEntity);
    }
}

/// <summary>
/// Force the respawn of a given entity at a given postion of kamasutra
/// </summary>
public struct ForceSpawnPosition : IComponentData
{
    public float3 Position;
    public quaternion Rotation;
}
