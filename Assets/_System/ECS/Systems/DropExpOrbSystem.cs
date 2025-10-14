using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;
using Unity.Collections;
using Unity.Mathematics;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(EntityDestructionSystem))]
[BurstCompile]
public partial struct DropExpOrbSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<OrbDatabaseBufferElement>();
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

        var dropExpJob = new DropExpOrbJob
        {
            ECB = ecb.AsParallelWriter(),
            OrbDatabase = orbsDatabase,
            Seed = (uint)(SystemAPI.Time.ElapsedTime * 1000) + 1
        };
        state.Dependency = dropExpJob.ScheduleParallel(state.Dependency);
    }


    [WithAll(typeof(ExperienceLoot), typeof(DestroyEntityFlag))]
    [WithNone(typeof(LootHasBeenDroppedTag))]
    private partial struct DropExpOrbJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter ECB;
        [ReadOnly] public DynamicBuffer<OrbDatabaseBufferElement> OrbDatabase;
        public uint Seed;

        public void Execute([ChunkIndexInQuery] int chunkIndex, Entity entity, in LocalTransform transform, in ExperienceLoot loot)
        {
            var rand = Random.CreateFromIndex(Seed);
            var dropChancePicked = rand.NextFloat();
            if (dropChancePicked > loot.DropChance)
            {
                ECB.AddComponent(chunkIndex, entity, new LootHasBeenDroppedTag());
                return;
            }

            int totalExp = loot.ExperienceValue;

            for (int i = 0; i < OrbDatabase.Length; i++)
            {
                OrbDatabaseBufferElement orbDb = OrbDatabase[i];

                if (orbDb.Value == 0)
                    continue;

                int numToDrop = totalExp / orbDb.Value;

                if (numToDrop > 0)
                {
                    for (int j = 0; j < numToDrop; j++)
                    {
                        var orb = ECB.Instantiate(chunkIndex, orbDb.Prefab);

                        var offset = rand.NextFloat2Direction() * 3;
                        var spawnPos = transform.Position + new float3(offset.x, 0, offset.y);
                        ECB.SetComponent(chunkIndex, orb, new LocalTransform()
                        {
                            Position = spawnPos,
                            Rotation = quaternion.identity,
                            Scale = 3,
                        });

                    }
                    totalExp = totalExp % orbDb.Value;
                }
            }

            ECB.AddComponent<LootHasBeenDroppedTag>(chunkIndex, entity);
        }
    }
}
