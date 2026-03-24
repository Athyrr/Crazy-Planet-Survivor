using _System.ECS.Authorings.Ressources;
using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;
using Unity.Collections;
using Unity.Mathematics;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(EntityDestructionSystem))]
[BurstCompile]
public partial struct DropLootSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<OrbDatabaseBufferElement>();
        state.RequireForUpdate<RessourcesDatabaseBufferElement>();
    }

    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton<GameState>(out var gameState))
            return;

        if (gameState.State != EGameState.Running)
            return;

        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        var orbsDatabase = SystemAPI.GetSingletonBuffer<OrbDatabaseBufferElement>(true);
        var ressourcesDatabase = SystemAPI.GetSingletonBuffer<RessourcesDatabaseBufferElement>(true);

        var dropExpJob = new DropExpOrbJob
        {
            ECB = ecb.AsParallelWriter(),
            OrbDatabase = orbsDatabase,
            RessourcesDatabaseBufferElements = ressourcesDatabase,
            Seed = (uint)(SystemAPI.Time.ElapsedTime * 1000) + 1
        };
        state.Dependency = dropExpJob.ScheduleParallel(state.Dependency);
    }


    [WithAll(typeof(Loot), typeof(DestroyEntityFlag))]
    [WithNone(typeof(LootHasBeenDroppedTag))]
    private partial struct DropExpOrbJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter ECB;
        [ReadOnly] public DynamicBuffer<OrbDatabaseBufferElement> OrbDatabase;
        [ReadOnly] public DynamicBuffer<RessourcesDatabaseBufferElement> RessourcesDatabaseBufferElements;
        public uint Seed;
        
        private Entity _instantiateEntity;
        private bool _entityFind;

        public void Execute([ChunkIndexInQuery] int chunkIndex, Entity entity, in LocalTransform transform, in Loot loot)
        {
            var rand = Random.CreateFromIndex(Seed);
            
            var dropChancePicked = rand.NextFloat();
            if (dropChancePicked > loot.DropChance)
            {
                ECB.AddComponent(chunkIndex, entity, new LootHasBeenDroppedTag());
                return;
            }

            int lootValue = loot.Value;

            if (loot.Type == ERessourceType.Xp)
            {
                //@todo benchmark
                for (int i = 0; i < OrbDatabase.Length; i++)
                {
                    OrbDatabaseBufferElement orbDb = OrbDatabase[i];

                    if (orbDb.Value <= 0)
                        continue;

                    int numToDrop = lootValue / orbDb.Value;

                    if (numToDrop > 0)
                    {
                        for (int j = 0; j < numToDrop; j++)
                        {
                            _instantiateEntity = ECB.Instantiate(chunkIndex, orbDb.Prefab);
                            _entityFind = true;
                            break;
                        }

                        lootValue %= orbDb.Value;
                    }
                }
            }
            else
            {
                RessourcesDatabaseBufferElement ressourceDb = RessourcesDatabaseBufferElements[(int)loot.Type];

                int numToDrop = lootValue / ressourceDb.Value;

                if (numToDrop > 0)
                {
                    for (int j = 0; j < numToDrop; j++)
                    {
                        _instantiateEntity = ECB.Instantiate(chunkIndex, ressourceDb.Prefab);
                        _entityFind = true;
                        break;
                    }

                    lootValue %= ressourceDb.Value;
                }
            }
            
            if (_entityFind)
            {
                var offset = rand.NextFloat2Direction() * 3;
                var spawnPos = transform.Position + new float3(offset.x, 0, offset.y);
                ECB.SetComponent(chunkIndex, _instantiateEntity, new LocalTransform()
                {
                    Position = spawnPos,
                    Rotation = quaternion.identity,
                    Scale = 1,
                });
            }

            _entityFind = false;
            ECB.AddComponent<LootHasBeenDroppedTag>(chunkIndex, entity);
        }
    }
}
