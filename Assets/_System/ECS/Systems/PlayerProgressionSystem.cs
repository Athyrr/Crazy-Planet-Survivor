using Unity.Burst;
using Unity.Entities;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[BurstCompile]
public partial struct PlayerProgressionSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PlayerExperience>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton<GameState>(out var gameState))
            return;

        if (gameState.State != EGameState.Running)
            return;

        if (!SystemAPI.TryGetSingletonEntity<Player>(out var player))
            return;

        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter();

        var gainExpJob = new GainExperienceJob()
        {
            ECB = ecb,
        };
        state.Dependency = gainExpJob.ScheduleParallel(state.Dependency);

        // Stats Tracking
        if (SystemAPI.TryGetSingletonRW<RunProgression>(out var progression))
        {
            var expQuery = SystemAPI.QueryBuilder().WithAll<Player, CollectedExperienceBufferElement>().Build();
            if (!expQuery.IsEmptyIgnoreFilter)
            {
                var buffers = expQuery.GetBufferLookup<CollectedExperienceBufferElement>(true);
                var entities = expQuery.ToEntityArray(Allocator.Temp);
                foreach (var e in entities)
                {
                    foreach (var exp in buffers[e])
                        progression.ValueRW.TotalExperienceCollected += exp.Value;
                }
            }
        }
    }

    [BurstCompile]
    private partial struct GainExperienceJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter ECB;

        public void Execute([ChunkIndexInQuery] int chunkIndex, in Entity entity, ref PlayerExperience experience, ref DynamicBuffer<CollectedExperienceBufferElement> expBuffer)
        {
            foreach (var exp in expBuffer)
            {
                experience.Experience += exp.Value;
            }
            expBuffer.Clear();

            if (experience.Experience >= experience.NextLevelExperienceRequired)
            {
                experience.Experience -= experience.NextLevelExperienceRequired;
                experience.Level++;

                //float nextLevelExperience = (experience.NextLevelExperienceRequired + (experience.NextLevelExperienceRequired * experience.Level * 0.5f)) + 1000;
                float nextLevelExperience = experience.Level * 500 + (experience.NextLevelExperienceRequired * 0.5f) + 1000;
                experience.NextLevelExperienceRequired = (int)nextLevelExperience;

                ECB.AddComponent(chunkIndex, entity, new PlayerLevelUpRequest() { });
            }
        }
    }
}
