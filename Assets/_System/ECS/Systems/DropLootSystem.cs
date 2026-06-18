using _System.ECS.Authorings.Resources;
using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;
using Unity.Collections;
using Unity.Mathematics;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(HealthSystem))]
[UpdateBefore(typeof(EntityDestructionSystem))]
[BurstCompile]
public partial struct DropLootSystem : ISystem
{
    private ComponentLookup<DestroyEntityFlag> _destroyFlagLookup;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        state.RequireForUpdate<ExpOrbDatabaseBufferElement>();
        state.RequireForUpdate<ResourcesDatabaseBufferElement>();
        state.RequireForUpdate<PlanetData>();

        _destroyFlagLookup = state.GetComponentLookup<DestroyEntityFlag>(isReadOnly: true);
    }

    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton<GameState>(out var gameState))
            return;

        if (gameState.State != EGameState.Running)
            return;

        _destroyFlagLookup.Update(ref state);

        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        var expOrbDatabase = SystemAPI.GetSingletonBuffer<ExpOrbDatabaseBufferElement>(true);
        var resourcesDatabase = SystemAPI.GetSingletonBuffer<ResourcesDatabaseBufferElement>(true);

        var planetCenter = SystemAPI.GetSingleton<PlanetData>().Center;

        state.Dependency = new DropLootJob
        {
            ECB = ecb.AsParallelWriter(),
            ExpOrbDatabase = expOrbDatabase,
            ResourcesDatabase = resourcesDatabase,
            DestroyFlagLookup = _destroyFlagLookup,
            PlanetCenter = planetCenter,
        }.ScheduleParallel(state.Dependency);
    }

    [WithAll(typeof(LootSource))]
    [WithNone(typeof(LootHasBeenDroppedTag))]
    private partial struct DropLootJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter ECB;

        [ReadOnly] public DynamicBuffer<ExpOrbDatabaseBufferElement> ExpOrbDatabase;
        [ReadOnly] public DynamicBuffer<ResourcesDatabaseBufferElement> ResourcesDatabase;
        [ReadOnly] public ComponentLookup<DestroyEntityFlag> DestroyFlagLookup;
        [ReadOnly] public float3 PlanetCenter;

        // Small radial lift so dropped loot sits just above the surface instead of clipping into it.
        private const float GroundOffset = 0.3f;

        private void Execute([ChunkIndexInQuery] int chunkIndex, Entity entity, in LocalTransform transform,
            in LootSource loot)
        {
            // Only process entities that are flagged for destruction (just died)
            if (!DestroyFlagLookup.IsComponentEnabled(entity))
                return;

            var rand = Random.CreateFromIndex((uint)(chunkIndex * 7919 + entity.Index * 104729));

            // Drop chance roll
            if (rand.NextFloat() > loot.DropChance)
            {
                ECB.AddComponent(chunkIndex, entity, new LootHasBeenDroppedTag());
                return;
            }

            if (loot.IsExperience)
                DropExpOrbs(chunkIndex, loot.Value, transform.Position, ref rand);
            else
                DropResources(chunkIndex, loot.Type, loot.Value, transform.Position, ref rand);

            ECB.AddComponent<LootHasBeenDroppedTag>(chunkIndex, entity);
        }

        private void DropExpOrbs(int chunkIndex, int lootValue, float3 origin, ref Random rand)
        {
            for (int i = 0; i < ExpOrbDatabase.Length; i++)
            {
                var entry = ExpOrbDatabase[i];
                if (entry.Value <= 0)
                    continue;

                int numToDrop = lootValue / entry.Value;
                if (numToDrop <= 0)
                    continue;

                for (int j = 0; j < numToDrop; j++)
                {
                    var expEntity = ECB.Instantiate(chunkIndex, entry.Prefab);
                    var offset = rand.NextFloat2Direction() * 3;
                    ECB.SetComponent(chunkIndex, expEntity, LocalTransform.FromPositionRotationScale(
                        GetSurfaceDropPosition(origin, offset),
                        quaternion.identity,
                        1f
                    ));
                }

                lootValue %= entry.Value;
            }
        }

        private void DropResources(int chunkIndex, EResourceType type, int lootValue, float3 origin, ref Random rand)
        {
            // Find the prefab matching this resource type from the database
            Entity prefab = Entity.Null;
            for (int i = 0; i < ResourcesDatabase.Length; i++)
            {
                if (ResourcesDatabase[i].Type == type)
                {
                    prefab = ResourcesDatabase[i].Prefab;
                    break;
                }
            }

            if (prefab == Entity.Null)
                return;

            // Each resource orb is worth exactly 1 → instantiate lootValue orbs
            for (int j = 0; j < lootValue; j++)
            {
                Entity spawned = ECB.Instantiate(chunkIndex, prefab);
                var offset = rand.NextFloat2Direction() * 3;
                ECB.SetComponent(chunkIndex, spawned, LocalTransform.FromPositionRotationScale(
                    GetSurfaceDropPosition(origin, offset),
                    quaternion.identity,
                    1f
                ));
            }
        }

        /// <summary>
        /// Places a dropped orb on the planet surface around <paramref name="origin"/>.
        /// The random offset is applied in the surface tangent plane (instead of flat world XZ),
        /// then reprojected onto the spherical shell at the drop height so it follows the
        /// curvature, plus a small radial lift to keep it from clipping into the ground.
        /// </summary>
        private float3 GetSurfaceDropPosition(float3 origin, float2 offset)
        {
            float3 up = math.normalize(origin - PlanetCenter);

            // Build a tangent basis around the surface normal.
            float3 tangent = math.cross(up, new float3(0f, 1f, 0f));
            if (math.lengthsq(tangent) < 0.001f)
                tangent = math.cross(up, new float3(1f, 0f, 0f));
            tangent = math.normalize(tangent);
            float3 bitangent = math.cross(up, tangent);

            float3 rough = origin + tangent * offset.x + bitangent * offset.y;

            // Keep the orb at the origin's surface height (+ a small lift), reprojected onto the sphere.
            float radius = math.distance(origin, PlanetCenter) + GroundOffset;
            return PlanetCenter + math.normalize(rough - PlanetCenter) * radius;
        }
    }
}