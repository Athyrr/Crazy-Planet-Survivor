using Unity.Burst;
using Unity.Entities;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[BurstCompile]
public partial struct GlobalIFramesSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float dt = SystemAPI.Time.DeltaTime;
        foreach (var (iframes, enabled) in SystemAPI.Query<RefRW<GlobalIFrames>, EnabledRefRW<GlobalIFrames>>())
        {
            iframes.ValueRW.RemainingTime -= dt;
            if (iframes.ValueRO.RemainingTime <= 0f)
                enabled.ValueRW = false;
        }
    }
}
