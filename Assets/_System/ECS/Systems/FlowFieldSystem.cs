using Unity.Burst;
using Unity.Entities;

[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
[UpdateAfter(typeof(CollisionSystem))]
[BurstCompile]
public partial struct FLowFieldSystem : ISystem
{
    private ComponentLookup<Player> _playerLookup;
    private ComponentLookup<Enemy> _enemyLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        _playerLookup = state.GetComponentLookup<Player>(true);
        _enemyLookup = state.GetComponentLookup<Enemy>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        
    }
}