using Unity.Mathematics;
using Unity.Transforms;
using Unity.Entities;
using Unity.Burst;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[BurstCompile]
public partial struct LobbyInteractionSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Interactable>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton<GameState>(out var gameState))
            return;

        //@todo
        //if (gameState.State != EGameState.Lobby)
        //    return;

        //if (!SystemAPI.TryGetSingletonEntity<Player>(out var playerEntity))
        //    return;

        if (!SystemAPI.TryGetSingletonEntity<Player>(out var playerEntity))
            return;

        var playerTransform = SystemAPI.GetComponent<LocalTransform>(playerEntity);

        bool isInteractPressed = false;
        if (!SystemAPI.TryGetSingleton<InputData>(out InputData input))
            return;

        isInteractPressed = input.IsInteractPressed;

        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        foreach (var (interactable, transform, interactableEntity) in SystemAPI.Query<RefRO<Interactable>, RefRO<LocalTransform>>().WithEntityAccess())
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
                // Load planets map + select + Launch
                ecb.AddComponent<OpenPlanetSelectionViewRequest>(eventEntity);
                break;
            case EInteractionType.Shop:
                //ecb.AddComponent<UI_DisplayShopMenuRequest>(eventEntity);
                break;
            case EInteractionType.CharacterSelection:
                ecb.AddComponent<OpenCharactersViewRequest>(eventEntity);
                break;
            case EInteractionType.AmuletShop:
                ecb.AddComponent<OpenAmuletShopViewRequest>(eventEntity);
                break;
        }
    }
}
