using Unity.Entities;
using UnityEngine;

/// <summary>Put on the enemy ghost prefab root (next to GhostAuthoringComponent, Interpolated).</summary>
public class NetEnemyAuthoring : MonoBehaviour
{
    public float Speed = 4f;
    public float StopDistance = 1.5f;

    private class Baker : Baker<NetEnemyAuthoring>
    {
        public override void Bake(NetEnemyAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new NetEnemy { Speed = authoring.Speed, StopDistance = authoring.StopDistance });
        }
    }
}
