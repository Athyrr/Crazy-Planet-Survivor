using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;
using UnityEngine;

[UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
[BurstCompile]
public partial struct PlayerSpawnerSystem : ISystem
{
    private EntityQuery _playerQuery;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameState>();
        state.RequireForUpdate<AmuletsDatabase>();
        state.RequireForUpdate<CharactersDatabase>();
        state.RequireForUpdate<SelectedCharacter>();

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

        if (!SystemAPI.TryGetSingleton<SelectedCharacter>(out SelectedCharacter selection))
            return;

        if (!SystemAPI.TryGetSingleton<PlanetData>(out PlanetData planetData))
            return;

        var amuletDatabaseEntity = SystemAPI.GetSingletonEntity<AmuletsDatabase>();
        if (amuletDatabaseEntity == Entity.Null)
            return;

        var amuletDatabaseRef = SystemAPI.GetComponent<AmuletsDatabase>(amuletDatabaseEntity);
        ref var amuletDatabase = ref amuletDatabaseRef.Blobs.Value.Amulets;

        var databaseEntity = SystemAPI.GetSingletonEntity<CharactersDatabase>();
        var characterPrefabsBuffer = SystemAPI.GetBuffer<CharacterPrefabBufferElement>(databaseEntity);

        if (selection.DbIndex < 0 || selection.DbIndex >= characterPrefabsBuffer.Length)
            return;

        var prefabToSpawn = characterPrefabsBuffer[selection.DbIndex].CharacterPrefabEntity;
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

        if (SystemAPI.TryGetSingleton<EquippedAmulet>(out var equippedAmulet) && equippedAmulet.DbIndex > -1)
        {
            // Request to Apply amulet modifiers
            state.EntityManager.AddComponentData(playerEntity, new ApplyAmuletRequest 
            { 
                DatabaseIndex = equippedAmulet.DbIndex 
            });
        }

        // Apply meta-progression bonuses
        var gameStateEntity = SystemAPI.GetSingletonEntity<GameState>();
        if (SystemAPI.HasBuffer<MetaProgressionLevelElement>(gameStateEntity))
        {
            var metaBuffer = SystemAPI.GetBuffer<MetaProgressionLevelElement>(gameStateEntity);
            var metaRequest = new ApplyMetaProgressionRequest();

            for (int i = 0; i < metaBuffer.Length; i++)
            {
                var meta = metaBuffer[i];
                if (meta.Level <= 0 || meta.TotalBonus == 0f)
                    continue;

                ApplyMetaBonus(ref metaRequest, meta.Stat, meta.TotalBonus);
            }

            state.EntityManager.AddComponentData(playerEntity, metaRequest);
        }
    }

    // todo handle while several bonuses have to be applied 
    private static void ApplyMetaBonus(ref ApplyMetaProgressionRequest request, ECharacterStat stat, float bonus)
    {
        switch (stat)
        {
            case ECharacterStat.MaxHealth:      request.MaxHealthBonus += bonus; break;
            case ECharacterStat.Speed:          request.MoveSpeedBonus += bonus; break;
            case ECharacterStat.Damage:         request.DamageBonus += bonus; break;
            case ECharacterStat.Armor:          request.ArmorBonus += bonus; break;
            case ECharacterStat.AttackSpeed:    request.AttackSpeedBonus += bonus; break;
            case ECharacterStat.SizeMultiplier: request.SpellSizeBonus += bonus; break;
            case ECharacterStat.CollectRange:   request.PickupRangeBonus += bonus; break;
            case ECharacterStat.BounceCount:    request.BounceBonus += (int)bonus; break;
            case ECharacterStat.PierceCount:    request.PierceBonus += (int)bonus; break;
            case ECharacterStat.CritChance:     request.CritChanceBonus += bonus; break;
            case ECharacterStat.CritDamage:     request.CritDamageBonus += bonus; break;
            case ECharacterStat.SpellSpeed:     request.SpellSpeedBonus += bonus; break;
        }
    }
}