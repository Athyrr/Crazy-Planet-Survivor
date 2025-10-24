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
        if (!SystemAPI.TryGetSingleton<GameState>(out var gameState))
            return;

        if (gameState.State != EGameState.Running)
            return;

        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        var simulationSingleton = SystemAPI.GetSingleton<SimulationSingleton>();

        var collisionJob = new CollisionEventJob
        {
            ECB = ecb.AsParallelWriter(),
            PlayerLookup = SystemAPI.GetComponentLookup<Player>(true),
            EnemyLookup = SystemAPI.GetComponentLookup<Enemy>(true),
            ProjectileLookup = SystemAPI.GetComponentLookup<Projectile>(true),
            //ObstacleLookup = SystemAPI.GetComponentLookup<Obstacle>(true) 
        };
        state.Dependency = collisionJob.Schedule(simulationSingleton, state.Dependency);
    }

    [BurstCompile]
    private struct CollisionEventJob : ICollisionEventsJob
    {
        public EntityCommandBuffer.ParallelWriter ECB;
        [ReadOnly] public ComponentLookup<Player> PlayerLookup;
        [ReadOnly] public ComponentLookup<Enemy> EnemyLookup;
        [ReadOnly] public ComponentLookup<Projectile> ProjectileLookup;

        public void Execute(CollisionEvent collisionEvent)
        {
            Entity entityA = collisionEvent.EntityA;
            Entity entityB = collisionEvent.EntityB;

            // Case Projectile hits enemy
            if (TryResolveProjectileVsTarget(entityA, entityB, out var projectileEntity, out var enemyEntity))
            {
                HandleProjectileHitTarget(projectileEntity, enemyEntity);
            }
            // Case Projectile hits player
            else if (TryResolveProjectileVsTarget(entityA, entityB, out projectileEntity, out var playerEntity, true))
            {
                HandleProjectileHitTarget(projectileEntity, playerEntity);
            }
            // Case Projectile hits Obstacle
            //else if (TryResolveProjectileVsObstacle(entityA, entityB, out projectileEntity, out var obstacleEntity))
            //{
            //    HandleProjectileHitObstacle(projectileEntity);
            //}
        }

        private void HandleProjectileHitTarget(Entity projectile, Entity target)
        {
            var projectileData = ProjectileLookup[projectile];
            ECB.AppendToBuffer(0, target, new DamageBufferElement()
            {
                Damage = projectileData.Damage,
                Element = projectileData.Element
            });

            ECB.AddComponent(0, projectile, new DestroyEntityFlag());
        }

        //private void HandleProjectileHitObstacle(Entity projectile)
        //{
        //    ECB.AddComponent(0, projectile, new DestroyEntityFlag());
        //}

        private bool TryResolveProjectileVsTarget(Entity entityA, Entity entityB, out Entity projectile, out Entity target, bool targetIsPlayer = false)
        {
            if (ProjectileLookup.HasComponent(entityA) && (targetIsPlayer ? PlayerLookup.HasComponent(entityB) : EnemyLookup.HasComponent(entityB)))
            {
                projectile = entityA;
                target = entityB;
                return true;
            }

            if (ProjectileLookup.HasComponent(entityB) && (targetIsPlayer ? PlayerLookup.HasComponent(entityA) : EnemyLookup.HasComponent(entityA)))
            {
                projectile = entityB;
                target = entityA;
                return true;
            }

            projectile = Entity.Null;
            target = Entity.Null;
            return false;
        }

        //private bool TryResolveProjectileVsObstacle(Entity entityA, Entity entityB, out Entity projectile, out Entity obstacle)
        //{
        //    if (ProjectileLookup.HasComponent(entityA) && ObstacleLookup.HasComponent(entityB))
        //    {
        //        projectile = entityA;
        //        obstacle = entityB;
        //        return true;
        //    }

        //    if (ProjectileLookup.HasComponent(entityB) && ObstacleLookup.HasComponent(entityA))
        //    {
        //        projectile = entityB;
        //        obstacle = entityA;
        //        return true;
        //    }

        //    projectile = Entity.Null;
        //    obstacle = Entity.Null;
        //    return false;
        //}
    }
}