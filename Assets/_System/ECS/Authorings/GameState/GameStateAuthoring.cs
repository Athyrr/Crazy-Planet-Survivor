using Unity.Entities;
using UnityEngine;

public class GameStateAuthoring : MonoBehaviour
{
    private class Baker : Baker<GameStateAuthoring>
    {
        public override void Bake(GameStateAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);

            // Store game state
            AddComponent(entity, new GameState { State = EGameState.Lobby });

            // Store upgrade selection 
            AddBuffer<UpgradeSelectionBufferElement>(entity);

            // Store selected character
            AddComponent(entity, new SelectedCharacterData
            {
                CharacterIndex = 0,
            });

            // Store wearable amulets
            AddBuffer<UnlockedAmulet>(entity);
            
            // Store selected amulet
            AddComponent(entity, new EquippedAmulet() {DbIndex = -1});
        }
    }
}