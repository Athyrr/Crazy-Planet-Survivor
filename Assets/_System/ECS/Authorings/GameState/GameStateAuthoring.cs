using Unity.Entities;
using UnityEngine;

public class GameStateAuthoring : MonoBehaviour
{
    public EGameState InitialState = EGameState.Running;

    private class Baker : Baker<GameStateAuthoring>
    {
        public override void Bake(GameStateAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);

            AddComponent(entity, new GameState { State = authoring.InitialState });

            // Store upgrade selection 
            AddBuffer<UpgradeSelectionBufferElement>(entity);
        }
    }
}