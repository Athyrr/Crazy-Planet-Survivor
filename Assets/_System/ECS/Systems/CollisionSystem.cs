using Unity.Jobs;
using Unity.Burst;
using Unity.Physics;
using Unity.Entities;
using Unity.Collections;
using Unity.Physics.Systems;

[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
[UpdateAfter(typeof(PhysicsSystemGroup))]
[BurstCompile]
public partial struct CollisionSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PhysicsStep>();
        state.RequireForUpdate<SimulationSingleton>();
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        var simulationSingleton = SystemAPI.GetSingleton<SimulationSingleton>();

        var collisionJob = new CollisionEventJob
        {
            ECB = ecb.AsParallelWriter(),
            EnemyLookup = SystemAPI.GetComponentLookup<Enemy>(true),
            ProjectileLookup = SystemAPI.GetComponentLookup<Projectile>(true)
        };
        state.Dependency = collisionJob.Schedule(simulationSingleton, state.Dependency);
    }

    [BurstCompile]
    private struct CollisionEventJob : ICollisionEventsJob
    {
        public EntityCommandBuffer.ParallelWriter ECB;
        [ReadOnly] public ComponentLookup<Enemy> EnemyLookup;
        [ReadOnly] public ComponentLookup<Projectile> ProjectileLookup;

        public void Execute(CollisionEvent collisionEvent)
        {
            Entity entityA = collisionEvent.EntityA;
            Entity entityB = collisionEvent.EntityB;

            bool isBodyAEnemy = EnemyLookup.HasComponent(entityA);
            bool isBodyBEnemy = EnemyLookup.HasComponent(entityB);
            bool isBodyAProjectile = ProjectileLookup.HasComponent(entityA);
            bool isBodyBProjectile = ProjectileLookup.HasComponent(entityB);

            // A: Projectile | B: Enemy
            if (isBodyAProjectile && isBodyBEnemy)
            {
                var projectileData = ProjectileLookup[entityA];
                ECB.AppendToBuffer(0, entityB, new DamageBufferElement()
                {
                    Damage = projectileData.Damage,
                    Element = projectileData.Element
                });

                ECB.AddComponent(0, entityA, new DestroyEntityFlag());
            }

            // A: Enemy | B: Projectile
            if (isBodyAEnemy && isBodyBProjectile)
            {
                var projectileData = ProjectileLookup[entityB];
                ECB.AppendToBuffer(0, entityA, new DamageBufferElement()
                {
                    Damage = projectileData.Damage,
                    Element = projectileData.Element
                });

                ECB.AddComponent(0, entityB, new DestroyEntityFlag());
            }
        }
    }
}