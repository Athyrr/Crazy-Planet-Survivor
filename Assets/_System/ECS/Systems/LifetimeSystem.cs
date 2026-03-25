using Unity.Burst;
using Unity.Entities;
using UnityEngine;

[BurstCompile]
public partial struct LifetimeSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        state.RequireForUpdate<Lifetime>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton<GameState>(out var gameState))
            return;

        if (gameState.State != EGameState.Running)
            return;

        var delta = SystemAPI.Time.DeltaTime;

        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        // Destroy entities with Lifetime component
        var lifetimeJob = new LifetimeJob
        {
            ECB = ecb.AsParallelWriter(),
            DeltaTime = delta
        };
        state.Dependency = lifetimeJob.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    [WithAll(typeof(Lifetime))]
    [WithDisabled(typeof(DestroyEntityFlag))]
    private partial struct LifetimeJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter ECB;
        public float DeltaTime;

        void Execute([ChunkIndexInQuery] int chunkIndex, Entity entity, ref Lifetime lifetime)
        {
            // Decrease time to live
            lifetime.TimeLeft -= DeltaTime; // todo check, mb never trigger by spell

            if (lifetime.TimeLeft <= 0f)
            {
                ECB.SetComponentEnabled<DestroyEntityFlag>(chunkIndex, entity, true);
            }
        }
    }
}
