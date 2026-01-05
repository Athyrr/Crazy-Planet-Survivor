using Unity.Burst;
using Unity.Entities;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[BurstCompile]
public partial struct SpawnDelaySystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float dt = SystemAPI.Time.DeltaTime;
        var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
            .CreateCommandBuffer(state.WorldUnmanaged);

        foreach (var (delay, entity) in SystemAPI.Query<RefRW<SpawnDelay>>()
                     .WithEntityAccess()
                     .WithOptions(EntityQueryOptions.IncludeDisabledEntities))
        {
            delay.ValueRW.Timer -= dt;
            if (delay.ValueRO.Timer > 0)
                continue;

            ecb.RemoveComponent<SpawnDelay>(entity);
            ecb.RemoveComponent<Disabled>(entity);
        }
    }
}

