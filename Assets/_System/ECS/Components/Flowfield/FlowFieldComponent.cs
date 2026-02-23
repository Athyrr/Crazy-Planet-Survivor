using Unity.Burst;
using Unity.Entities;

[BurstCompile]
public partial struct FlowFieldSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<FlowField>();
    }
}
