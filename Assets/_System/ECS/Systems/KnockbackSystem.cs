using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(EntitiesMovementSystem))]
[BurstCompile]
public partial struct KnockbackSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PhysicsWorldSingleton>();
        state.RequireForUpdate<PlanetData>();
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        var deltaTime = SystemAPI.Time.DeltaTime;

        var planetEntity = SystemAPI.GetSingletonEntity<PlanetData>();
        var planetPos = SystemAPI.GetComponent<LocalTransform>(planetEntity).Position;

        var collisionWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld;

        new ProcessKnockbackJob
        {
            DeltaTime = deltaTime,
            PlanetPos = planetPos,
            CollisionWorld = collisionWorld,
            ECB = ecb.AsParallelWriter()
        }.ScheduleParallel();
    }

    [BurstCompile]
    private partial struct ProcessKnockbackJob : IJobEntity
    {
        public float DeltaTime;
        public float3 PlanetPos;
        [ReadOnly] public CollisionWorld CollisionWorld;
        public EntityCommandBuffer.ParallelWriter ECB;

        public void Execute(
            [ChunkIndexInQuery] int chunkIndex,
            Entity entity,
            ref LocalTransform transform,
            ref ActiveKnockback knockback,
            ref FinalStats finalStats) 
        {
            knockback.DurationLeft -= DeltaTime;

            if (knockback.DurationLeft <= 0)
            {
                ECB.RemoveComponent<ActiveKnockback>(chunkIndex, entity);
                return;
            }

            // Calculate the current knockback force based on the elapsed time
            float t = math.clamp(knockback.DurationLeft / knockback.MaxDuration, 0f, 1f);

            // Curve eases out
            float currentForce = knockback.InitialForce * (t * t);

            float3 desiredPos = transform.Position + (knockback.Direction * currentForce * DeltaTime);


            // Handle obstacles 

            // PlanetUtils.SnapToSurfaceRaycast()
            
            // Apply position
            transform.Position = desiredPos;
            
            // Set speed to 0 to prevent movement
            finalStats.MoveSpeed = 0;
        }
    }
}