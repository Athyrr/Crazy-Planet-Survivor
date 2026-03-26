using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

[UpdateAfter(typeof(DropLootSystem))]
[BurstCompile]
public partial struct EntityDestructionSystem : ISystem
{
    private BufferLookup<Child> _childLookup; 
        
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<BeginSimulationEntityCommandBufferSystem.Singleton>();
        state.RequireForUpdate<DestroyEntityFlag>();
        _childLookup = state.GetBufferLookup<Child>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton<GameState>(out var gameState))
            return;

        if (gameState.State != EGameState.Running)
            return;

        var ecbSingleton = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        _childLookup.Update(ref state);
        
        // Destroy entities with DestroyEntityFlag component
        var destructionJob = new DestructionJob
        {
            ECB = ecb.AsParallelWriter(),
            ChildLookup = _childLookup
        };
        state.Dependency = destructionJob.ScheduleParallel(state.Dependency);
    }

    [WithAll(typeof(DestroyEntityFlag))]
    [BurstCompile]
    private partial struct DestructionJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter ECB;
        [ReadOnly] public BufferLookup<Child> ChildLookup;
        
        void Execute([ChunkIndexInQuery] int chunkIndex, Entity entity, in DestroyEntityFlag destroyFlag)
        {
            NativeList<Entity> entitiesToDestroy = new NativeList<Entity>(Allocator.Temp);
            entitiesToDestroy.Add(entity);
            
            for (int i = 0; i < entitiesToDestroy.Length; i++)
            {
                Entity currentEntity = entitiesToDestroy[i];
                
                if (ChildLookup.TryGetBuffer(currentEntity, out DynamicBuffer<Child> children))
                {
                    for (int j = 0; j < children.Length; j++)
                    {
                        entitiesToDestroy.Add(children[j].Value);
                    }
                }
            }

            for (int i = entitiesToDestroy.Length - 1; i >= 0; i--)
            {
                ECB.DestroyEntity(chunkIndex, entitiesToDestroy[i]);
            }

            ECB.DestroyEntity(chunkIndex, entity);
        }
    }
}

