using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[BurstCompile]
public partial struct EnemiesMovementRequestProviderSystem : ISystem
{

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Enemy>();
        state.RequireForUpdate<Player>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingletonEntity<Player>(out Entity playerEntity))
            return;

        float3 playerPosition = SystemAPI.GetComponentRO<LocalTransform>(playerEntity).ValueRO.Position;

        var ecbSystem = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        EntityCommandBuffer.ParallelWriter ecb = ecbSystem.CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter();

        var job = new GenerateMovementRequestForEntityJob()
        {
            PlayerPosition = playerPosition,
            ECB = ecb
        };

        state.Dependency = job.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    private partial struct GenerateMovementRequestForEntityJob : IJobEntity
    {
        public float3 PlayerPosition;
        public EntityCommandBuffer.ParallelWriter ECB;

        void Execute([EntityIndexInQuery] int index, Entity entity, in LocalTransform transform, in Velocity velocity, in Enemy enemy)
        {
            float3 position = transform.Position;
            float3 direction = PlayerPosition - position;

            ECB.AddComponent(index, entity, new RequestForMovement()
            {
                Direction = direction,
            });
        }
    }
}


