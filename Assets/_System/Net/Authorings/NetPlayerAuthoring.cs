using Unity.Entities;
using UnityEngine;

/// <summary>Put on the player ghost prefab root (next to GhostAuthoringComponent, Owner Predicted, Has Owner).</summary>
public class NetPlayerAuthoring : MonoBehaviour
{
    public float Speed = 9f;

    private class Baker : Baker<NetPlayerAuthoring>
    {
        public override void Bake(NetPlayerAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new NetPlayer { Speed = authoring.Speed });
            AddComponent<NetPlayerInput>(entity);
        }
    }
}
