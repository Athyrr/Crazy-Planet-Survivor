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

                float nextLevelExperience = (experience.NextLevelExperienceRequired + (experience.NextLevelExperienceRequired * experience.Level * 0.2f)) * 0.8f;
                experience.NextLevelExperienceRequired = (int)nextLevelExperience;

                ECB.AddComponent(chunkIndex, entity, new PlayerLevelUpRequest() { });
            }
        }
    }
}
