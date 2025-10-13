using Unity.Burst;
using Unity.Entities;

[UpdateAfter(typeof(DropExpOrbSystem))]
[BurstCompile]
public partial struct EntityDestructionSystem : ISystem
{

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<DestroyEntityFlag>();
    }

    [BurstCompile]
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

    [BurstCompile]
    private partial struct DestructionJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter ECB;
        void Execute([ChunkIndexInQuery] int chunkIndex, Entity entity, in DestroyEntityFlag destroyFlag)
        {
            ECB.DestroyEntity(chunkIndex, entity);
        }
    }
}

