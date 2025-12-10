using Unity.Entities;
using UnityEngine;

public class PlayerStartAuthoring : MonoBehaviour
{
    private class Baker : Baker<PlayerAuthoring>
    {
        public override void Bake(PlayerAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent<PlayerStart>(entity);
        }
    }
}
