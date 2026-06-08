using System;
using NUnit.Framework.Internal;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Entities;
using Unity.Burst;
using UnityEngine;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[BurstCompile]
public partial struct LobbyInteractionSystem : ISystem
{
    // Tracks lobby state across frames so an interact press that arrives on the same frame the
    // lobby is (re)entered — e.g. the one that confirmed/closed a shop — can be ignored.
    private bool _wasInLobby;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        state.RequireForUpdate<Interactable>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton<GameState>(out var gameState))
            return;
        
        if (gameState.State != EGameState.Lobby)
        {
            _wasInLobby = false;
            return;
        }

        // True only on the first frame after (re)entering the lobby.
        var justEnteredLobby = !_wasInLobby;
        _wasInLobby = true;

        if (!SystemAPI.TryGetSingletonEntity<Player>(out var playerEntity))
            return;

        var playerTransform = SystemAPI.GetComponent<LocalTransform>(playerEntity);

        var isInteractPressed = false;
        if (!SystemAPI.TryGetSingleton<InputData>(out InputData input))
            return;


        // Ignore the interact press on the frame we just (re)entered the lobby — that press is the
        // one that confirmed/closed a shop (shops confirm with Player.Interact), so it must not
        // instantly re-open the building the player is still standing on.
        isInteractPressed = input.IsInteractPressed && !justEnteredLobby;

        if (isInteractPressed)
            Debug.Log("Inputs triggered ECS");

        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        foreach (var (interactable, transform, interactableEntity) in SystemAPI
                     .Query<RefRO<Interactable>, RefRO<LocalTransform>>().WithEntityAccess())
        {
            var interactableRadius = interactable.ValueRO.Radius;
            float distSq = math.distancesq(playerTransform.Position, transform.ValueRO.Position);
            bool isInRange = distSq <= interactableRadius * interactableRadius;

            bool wasEnabled = SystemAPI.IsComponentEnabled<InteractableInRangeTag>(interactableEntity);
            if (wasEnabled != isInRange)
            {
                ecb.SetComponentEnabled<InteractableInRangeTag>(interactableEntity, isInRange);
            }

            if (isInRange && isInteractPressed)
            {
                isInteractPressed = false;
                PerformInteract(ecb, interactable.ValueRO.InteractionType, interactableEntity);
            }
        }
    }

    private void PerformInteract(EntityCommandBuffer ecb, EInteractionType interactionType, Entity interactableEntity)
    {
        UnityEngine.Debug.Log($"Interaction: {interactionType}");

        var eventEntity = ecb.CreateEntity();

        switch (interactionType)
        {
            case EInteractionType.PlanetSelection:
                ecb.AddComponent<OpenPlanetSelectionViewRequest>(eventEntity);
                break;
            case EInteractionType.MetaProgression:
                ecb.AddComponent<OpenMetaProgressionShopRequest>(eventEntity);
                break;
            case EInteractionType.CharacterSelection:
                ecb.AddComponent<OpenCharactersShopRequest>(eventEntity);
                break;
            case EInteractionType.AmuletShop:
                ecb.AddComponent<OpenAmuletShopRequest>(eventEntity);
                break;
        }
    }
}