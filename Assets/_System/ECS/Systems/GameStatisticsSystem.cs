using Unity.Entities;
using Unity.Burst;
using Unity.Collections;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[BurstCompile]
public partial struct GameStatisticsSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        // We do not require the component here because we want to create it if it's missing.
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.HasSingleton<GameStatistics>())
        {
            var entity = state.EntityManager.CreateEntity();
            state.EntityManager.AddComponentData(entity, new GameStatistics());
        }
    }
}
