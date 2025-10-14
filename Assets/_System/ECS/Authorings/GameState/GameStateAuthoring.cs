using Unity.Entities;
using UnityEngine;

public class GameStateAuthoring : MonoBehaviour
{
    private class Baker : Baker<GameStateAuthoring>
    {
        public override void Bake(GameStateAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);
            AddComponent(entity, new GameState { State = EGameState.Running});
        }
    }
}