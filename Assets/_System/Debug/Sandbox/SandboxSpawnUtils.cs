#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>Main-thread instantiation around the player (scene-de-test). Angular ring (heuristics: angles, never a
/// world-distance constant), evenly spaced bearings: reproducible placement for TTK measures.</summary>
public static class SandboxSpawnUtils
{
    public const float RingAngleRad = 0.25f;

    public static NativeArray<Entity> SpawnAroundPlayer(EntityManager em, Entity player, Entity prefab, int count,
        float heightOffset, Allocator allocator)
    {
        using var planetQuery = em.CreateEntityQuery(typeof(PlanetData));
        var planet = planetQuery.GetSingleton<PlanetData>();
        float3 playerPos = em.GetComponentData<LocalTransform>(player).Position;
        float3 up = math.normalize(playerPos - planet.Center);
        float3 reference = math.abs(up.y) < 0.99f ? new float3(0, 1, 0) : new float3(1, 0, 0);
        float3 tangent = math.normalize(math.cross(up, reference));
        float3 bitangent = math.cross(up, tangent);

        var entities = em.Instantiate(prefab, count, allocator);
        for (int i = 0; i < count; i++)
        {
            float bearing = 2f * math.PI * i / count;
            float3 side = tangent * math.cos(bearing) + bitangent * math.sin(bearing);
            float3 dir = math.normalize(up * math.cos(RingAngleRad) + side * math.sin(RingAngleRad));
            var t = em.GetComponentData<LocalTransform>(entities[i]);
            t.Position = planet.Center + dir * (planet.Radius + heightOffset);
            t.Rotation = quaternion.LookRotationSafe(math.normalizesafe(playerPos - t.Position), dir);
            em.SetComponentData(entities[i], t);
        }
        return entities;
    }

    /// <summary>Enemies at BASE stats (no HealthMult / DamageMult, unlike the spawner's SpawnJob), WaveIndex -1 (no wave
    /// credit), counted in SpawnerState.ActiveEnemyCount so the cap stays consistent.</summary>
    public static NativeArray<Entity> SpawnEnemies(EntityManager em, Entity player, Entity prefab, int count, Allocator allocator)
    {
        var spawned = SpawnAroundPlayer(em, player, prefab, count, 0.5f, allocator);
        for (int i = 0; i < spawned.Length; i++)
            em.SetComponentData(spawned[i], new Enemy { WaveIndex = -1 });

        using var stateQuery = em.CreateEntityQuery(typeof(SpawnerState));
        if (stateQuery.TryGetSingleton<SpawnerState>(out var spawnerState))
        {
            spawnerState.ActiveEnemyCount += count;
            stateQuery.SetSingleton(spawnerState);
        }
        return spawned;
    }
}
#endif
