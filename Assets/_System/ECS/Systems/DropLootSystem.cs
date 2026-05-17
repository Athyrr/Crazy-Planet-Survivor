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

        state.Dependency = new DropLootJob
        {
            ECB = ecb.AsParallelWriter(),
            ExpOrbDatabase = expOrbDatabase,
            ResourcesDatabase = resourcesDatabase,
            DestroyFlagLookup = _destroyFlagLookup,
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
                        origin + new float3(offset.x, 0, offset.y),
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
                    origin + new float3(offset.x, 0, offset.y),
                    quaternion.identity,
                    1f
                ));
            }
        }
    }
}