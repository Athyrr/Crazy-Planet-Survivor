using Unity.Collections;
using Unity.Mathematics;
using Unity.Transforms;
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

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<ClearRunRequest>();

        // Run scoped entities query excluding player
        //_runScopeEntitiesQuery = state.GetEntityQuery(
        //    ComponentType.ReadOnly<RunScope>(),
        //    ComponentType.Exclude<Player>());

        _runScopeEntitiesQuery = state.GetEntityQuery(
         ComponentType.ReadOnly<RunScope>());
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var ecbSingleton = SystemAPI.GetSingleton<EndInitializationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        // Destroy run scoped entities
        var entities = _runScopeEntitiesQuery.ToEntityArray(Allocator.Temp);
        state.EntityManager.DestroyEntity(entities);
        entities.Dispose();

        // Query player
        foreach (var (transform, health, baseStats, experience, statModifiers, activeSpells, expBuffer,/* damagesBuffer,*/ entity)
                in SystemAPI.Query<
                    RefRW<LocalTransform>,
                    RefRW<Health>,
                    RefRO<BaseStats>,
                    RefRW<PlayerExperience>,
                    DynamicBuffer<StatModifier>,
                    DynamicBuffer<ActiveSpell>,
                DynamicBuffer<CollectedExperienceBufferElement>
                //DynamicBuffer<DamageBufferElement>
                >()
                .WithAll<Player>()
                .WithEntityAccess())
        {
            if (state.EntityManager.HasComponent<DestroyEntityFlag>(entity))
                ecb.RemoveComponent<DestroyEntityFlag>(entity);

            // Reset position
            transform.ValueRW.Position = new float3(0, 51, 0); // @todo use proper spawn point
            transform.ValueRW.Rotation = quaternion.identity;

            // Reset Health
            health.ValueRW.Value = baseStats.ValueRO.MaxHealth;

            // Reset Stats Modifiers
            statModifiers.Clear();

            // Reset Active Spells
            activeSpells.Clear();
            var baseSpellsBuffer = state.EntityManager.GetBuffer<BaseSpell>(entity);
            var spellActivationBuffer = state.EntityManager.GetBuffer<SpellActivationRequest>(entity);
            foreach (var baseSpell in baseSpellsBuffer)
            {
                spellActivationBuffer.Add(new SpellActivationRequest
                {
                    ID = baseSpell.ID
                });
            }


            // Reset collected exp buffer
            expBuffer.Clear();

            // Reset Experience
            experience.ValueRW.Experience = 0;
            experience.ValueRW.Level = 1;

            // Force Recalculate Stats
            //state.EntityManager.AddComponent<RecalculateStatsRequest>(entity);
            ecb.AddComponent<RecalculateStatsRequest>(entity);
        }

        // Destroy clear run request
        var clearRequestEntity = SystemAPI.GetSingletonEntity<ClearRunRequest>();
        state.EntityManager.DestroyEntity(clearRequestEntity);
    }
}
