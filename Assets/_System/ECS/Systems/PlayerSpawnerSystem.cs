using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;

[UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
[BurstCompile]
public partial struct PlayerSpawnerSystem : ISystem
{
    private EntityQuery _playerQuery;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameState>();
        state.RequireForUpdate<CharactersDatabase>();
        state.RequireForUpdate<SelectedCharacterData>();

        _playerQuery = state.GetEntityQuery(ComponentType.ReadWrite<Player>());
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!_playerQuery.IsEmpty)
            return;

        if (!SystemAPI.TryGetSingleton<GameState>(out var gameState))
            return;

        if (gameState.State != EGameState.Lobby && gameState.State != EGameState.Running)
            return;

        if (!SystemAPI.TryGetSingleton<SelectedCharacterData>(out SelectedCharacterData selection))
            return;

        if (!SystemAPI.TryGetSingleton<PlanetData>(out PlanetData planetData))
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
            float3 direction = math.normalize(startPoint.Position - planetData.Center);
            //position = planetData.Center + (direction * (planetData.Radius + 1.0f));
            position = startPoint.Position;
            rot = quaternion.LookRotationSafe(math.forward(startPoint.Rotation), direction);
        }
        else
        {
            float3 up = new float3(0, 1, 0);
            position = planetData.Center + (up * (planetData.Radius + 2.0f));
            rot = quaternion.LookRotationSafe(new float3(0, 0, 1), up);
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