using Unity.Entities;
using UnityEngine;

public partial struct EntityDestructionSystem : ISystem
{

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<DestroyEntityFlag>();
    }

    public void OnUpdate(ref SystemState state)
    {
        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        // Destroy entities with DestroyEntityFlag component
        var destructionJob = new DestructionJob
        {
            ECB = ecb.AsParallelWriter()
        };
        state.Dependency = destructionJob.ScheduleParallel(state.Dependency);
    }

    private partial struct DestructionJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter ECB;
        void Execute([ChunkIndexInQuery] int chunkIndex, Entity entity, in DestroyEntityFlag destroyFlag)
        {
            ECB.DestroyEntity(chunkIndex, entity);
        }
    }
}

