using Unity.Collections;
using Unity.Entities;
using Unity.Burst;

/// <summary>
/// System that clear all run scoped entities before returning to lobby on player death/win.
/// </summary>
[UpdateInGroup(typeof(InitializationSystemGroup))]
[BurstCompile]
public partial struct RunCleanerSystem : ISystem
{
    private EntityQuery _runScopeEntitiesQuery;
    private EntityQuery _playerQuery;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<ClearRunRequest>();

        _runScopeEntitiesQuery = state.GetEntityQuery(ComponentType.ReadOnly<RunScope>());
        _playerQuery = state.GetEntityQuery(ComponentType.ReadOnly<Player>());
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // Destroy run scoped entities
        var entities = _runScopeEntitiesQuery.ToEntityArray(Allocator.Temp);
        state.EntityManager.DestroyEntity(entities);
        entities.Dispose();

        // Destroy Player
        state.EntityManager.DestroyEntity(_playerQuery);

        var requestEntity = SystemAPI.GetSingletonEntity<ClearRunRequest>();
        state.EntityManager.DestroyEntity(requestEntity);
    }
}
