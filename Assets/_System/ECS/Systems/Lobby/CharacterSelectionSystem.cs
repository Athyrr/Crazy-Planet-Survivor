using Unity.Transforms;
using Unity.Entities;
using Unity.Burst;

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
        var request = SystemAPI.GetComponent<SelectCharacterRequest>(selectCharacterRequestEntity);

        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        var databaseEntity = SystemAPI.GetSingletonEntity<CharactersDatabase>();
        var charactersBuffer = SystemAPI.GetBuffer<CharacterPrefabBufferElement>(databaseEntity);


        if (request.CharacterIndex >= 0 && request.CharacterIndex < charactersBuffer.Length)
        {
            var selectedCharacterPrefab = charactersBuffer[request.CharacterIndex].CharacterPrefabEntity;

            if (SystemAPI.TryGetSingletonEntity<Player>(out var currentPlayer))
            {
                // Store current player position
                var currentPlayerTransform = SystemAPI.GetComponent<LocalTransform>(currentPlayer);

                // Destroy current player
                ecb.DestroyEntity(currentPlayer);

                // Instantiate selected character
                var selectedPlayer = ecb.Instantiate(selectedCharacterPrefab);

                // Set position
                ecb.SetComponent(selectedPlayer, currentPlayerTransform);
            }
            // If no player yet
            else
            {
                var newPlayer = ecb.Instantiate(selectedCharacterPrefab);
                ecb.SetComponent(newPlayer, LocalTransform.FromPosition(0, 50.5f, 0)); // sclae to 1
            }
        }

        ecb.DestroyEntity(selectCharacterRequestEntity);
    }
}
