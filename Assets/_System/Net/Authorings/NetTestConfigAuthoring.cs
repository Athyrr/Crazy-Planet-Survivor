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
