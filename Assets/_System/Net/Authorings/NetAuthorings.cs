using Unity.Entities;
using UnityEngine;

/// <summary>Bakes <see cref="NetTestConfig"/> (prefab refs + spawn settings) for the net test sub scene.</summary>
public class NetTestConfigAuthoring : MonoBehaviour
{
    public GameObject PlayerPrefab;
    public GameObject EnemyPrefab;
    public float EnemySpawnMinDistance = 25f;
    public float EnemySpawnMaxDistance = 45f;
    public int MaxSpawnPerTick = 50;

    private class Baker : Baker<NetTestConfigAuthoring>
    {
        public override void Bake(NetTestConfigAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);
            AddComponent(entity, new NetTestConfig
            {
                PlayerPrefab = GetEntity(authoring.PlayerPrefab, TransformUsageFlags.Dynamic),
                EnemyPrefab = GetEntity(authoring.EnemyPrefab, TransformUsageFlags.Dynamic),
                EnemySpawnMinDistance = authoring.EnemySpawnMinDistance,
                EnemySpawnMaxDistance = authoring.EnemySpawnMaxDistance,
                MaxSpawnPerTick = authoring.MaxSpawnPerTick
            });
        }
    }
}

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
