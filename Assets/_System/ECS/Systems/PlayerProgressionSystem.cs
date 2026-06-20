using Unity.Burst;
using Unity.Entities;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(RessourceAttractionSystem))]
[BurstCompile]
public partial struct PlayerProgressionSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
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

        var gainExpJob = new GainExperienceJob { ECB = ecb };

        state.Dependency = gainExpJob.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    private partial struct GainExperienceJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter ECB;

        private void Execute([ChunkIndexInQuery] int chunkIndex, in Entity entity, ref PlayerExperience experience)
        {
            if (experience.Experience < experience.NextLevelExperienceRequired)
                return;

            experience.Experience -= experience.NextLevelExperienceRequired;
            experience.Level++;

            experience.NextLevelExperienceRequired = PlayerExperience.XpForNextLevel(experience.Level);

            ECB.AddComponent(chunkIndex, entity, new PlayerLevelUpRequest());
        }
    }
}
