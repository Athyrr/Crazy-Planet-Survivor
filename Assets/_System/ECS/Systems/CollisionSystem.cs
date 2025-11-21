using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Physics;
using Unity.Physics.Systems;
using Unity.Transforms;

[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
[UpdateAfter(typeof(PhysicsSystemGroup))]
[BurstCompile]
public partial struct CollisionSystem : ISystem
{
    ComponentLookup<Player> playerLookup;
    ComponentLookup<Enemy> enemyLookup;
    ComponentLookup<Projectile> projectileLookup;
    ComponentLookup<LocalTransform> transformLookup;
    ComponentLookup<RicochetData> ricochetLookup;
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PhysicsStep>();
        state.RequireForUpdate<SimulationSingleton>();
        state.RequireForUpdate<PhysicsWorldSingleton>();
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();

        playerLookup = state.GetComponentLookup<Player>(true);
        enemyLookup = state.GetComponentLookup<Enemy>(true);
        projectileLookup = state.GetComponentLookup<Projectile>(true);
        transformLookup = state.GetComponentLookup<LocalTransform>(true);
        ricochetLookup = state.GetComponentLookup<RicochetData>(false);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton<GameState>(out var gameState))
            return;

        if (gameState.State != EGameState.Running)
            return;

        playerLookup.Update(ref state);
        enemyLookup.Update(ref state);
        projectileLookup.Update(ref state);
        transformLookup.Update(ref state);
        ricochetLookup.Update(ref state);

        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        var simulationSingleton = SystemAPI.GetSingleton<SimulationSingleton>();
        var collisionWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld;

        var collisionJob = new CollisionEventJob
        {
            ECB = ecb.AsParallelWriter(),
            CollisionWorld = collisionWorld,

            PlayerLookup = playerLookup,
            EnemyLookup = enemyLookup,
            ProjectileLookup = projectileLookup,
            TransformLookup = transformLookup,
            //ObstacleLookup = obstacleLookup,
            Ricochetlookup = ricochetLookup
        };

        var triggerJob = new TriggerEventJob
        {
            ECB = ecb.AsParallelWriter(),

            PlayerLookup = playerLookup,
            EnemyLookup = enemyLookup,
            ProjectileLookup = projectileLookup,

        };

        var handleCollision = collisionJob.Schedule(simulationSingleton, state.Dependency);
        var handleTrigger = triggerJob.Schedule(simulationSingleton, handleCollision);

        state.Dependency = handleTrigger;
    }

    [BurstCompile]
    private struct CollisionEventJob : ICollisionEventsJob
    {
        public EntityCommandBuffer.ParallelWriter ECB;
        [ReadOnly] public CollisionWorld CollisionWorld;

        [ReadOnly] public ComponentLookup<Player> PlayerLookup;
        [ReadOnly] public ComponentLookup<Enemy> EnemyLookup;
        [ReadOnly] public ComponentLookup<Projectile> ProjectileLookup;
        [ReadOnly] public ComponentLookup<LocalTransform> TransformLookup;
        //[ReadOnly] public ComponentLookup<Obstacle> ObstacleLookup;

        public ComponentLookup<RicochetData> Ricochetlookup;

        public void Execute(CollisionEvent collisionEvent)
        {
            Entity entityA = collisionEvent.EntityA;
            Entity entityB = collisionEvent.EntityB;

            if (ProjectileLookup.HasComponent(entityA) || ProjectileLookup.HasComponent(entityA))
            {
                return;
            }

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
            // Apply damage to target
            var projectileData = ProjectileLookup[projectile];
            ECB.AppendToBuffer(0, target, new DamageBufferElement()
            {
                Damage = projectileData.Damage,
                Element = projectileData.Element
            });

            // Create feedback request  (screen shake)
            var feedabckEntity = ECB.CreateEntity(0);
            ECB.AddComponent(0, feedabckEntity, new ShakeFeedbackRequest());

            // Handle ricochet 
            if (Ricochetlookup.HasComponent(projectile))
            {
                HandleRicochet(projectile, target);
            }
            else
            {
                // Destroy the projectile
                ECB.AddComponent(0, projectile, new DestroyEntityFlag());
            }
        }

        //private void HandleProjectileHitObstacle(Entity projectile)
        //{
        //    ECB.AddComponent(0, projectile, new DestroyEntityFlag());
        //}

        /// <summary>
        /// Handles the ricochet logic when a projectile hits a target.
        /// ONLY HIT ENEMIES NOT PLAYER FOR NOW
        /// </summary>
        /// <param name="projectile"></param>
        /// <param name="targetHit"></param>
        /// <param name="contactPoint"></param>
        private void HandleRicochet(Entity projectile, Entity targetHit)
        {
            var ricochetData = Ricochetlookup[projectile];

            // No more bounces left, destroy the projectile
            if (ricochetData.RemainingBounces <= 0)
            {
                ECB.AddComponent(0, projectile, new DestroyEntityFlag());
                return;
            }

            // Decrease remaining bounces
            ricochetData.RemainingBounces--;
            // Update the ricochet data
            Ricochetlookup[projectile] = ricochetData;

            var hits = new NativeList<DistanceHit>(Allocator.Temp);
            var filter = new CollisionFilter
            {
                BelongsTo = CollisionLayers.Raycast,
                CollidesWith = CollisionLayers.Enemy,
            };

            var targetPosition = TransformLookup[targetHit].Position;
            // Detect nearby enemies 
            CollisionWorld.OverlapSphere(targetPosition, ricochetData.SearchRadius, ref hits, filter);

            float closestDistance = float.MaxValue;
            Entity nextTarget = Entity.Null;

            for (int i = 0; i < hits.Length; i++)
            {
                var hit = hits[i];
                // Skip the target that was just hit
                if (hit.Entity == targetHit)
                    continue;

                if (hit.Distance < closestDistance)
                {
                    closestDistance = hit.Distance;
                    nextTarget = hit.Entity;
                }
            }

            hits.Dispose();

            // If a new target is found, redirect the projectile
            if (nextTarget != Entity.Null)
            {
                ECB.RemoveComponent<LinearMovement>(0, projectile);
                ECB.AddComponent(0, projectile, new FollowTargetMovement
                {
                    Target = nextTarget,
                    Speed = ricochetData.Speed,
                    StopDistance = 0f

                });
            }
            else
            {
                ECB.AddComponent(0, projectile, new DestroyEntityFlag());
            }
        }

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

        //private bool TryResolveEnemyVsPlayer(Entity entityA, Entity entityB, out Entity enemy, out Entity player)
        //{
        //    if (EnemyLookup.HasComponent(entityA) && PlayerLookup.HasComponent(entityB))
        //    {
        //        enemy = entityA;
        //        player = entityB;
        //        return true;
        //    }

        //    if (EnemyLookup.HasComponent(entityB) && PlayerLookup.HasComponent(entityA))
        //    {
        //        enemy = entityB;
        //        player = entityA;
        //        return true;
        //    }

        //    enemy = Entity.Null;
        //    player = Entity.Null;
        //    return false;
        //}


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

    [BurstCompile]
    private struct TriggerEventJob : ITriggerEventsJob
    {
        public EntityCommandBuffer.ParallelWriter ECB;
        [ReadOnly] public ComponentLookup<Player> PlayerLookup;
        [ReadOnly] public ComponentLookup<Enemy> EnemyLookup;
        [ReadOnly] public ComponentLookup<Projectile> ProjectileLookup;

        public void Execute(TriggerEvent triggerEvent)
        {
            Entity entityA = triggerEvent.EntityA;
            Entity entityB = triggerEvent.EntityB;

            // Case Projectile hits enemy
            if (TryResolveProjectileVsTarget(entityA, entityB, out var projectileEntity, out var enemyEntity))
            {
                HandleProjectileHitTarget(projectileEntity, enemyEntity);
            }            // Case Projectile hits player

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

        //private bool TryResolveEnemyVsPlayer(Entity entityA, Entity entityB, out Entity enemy, out Entity player)
        //{
        //    if (EnemyLookup.HasComponent(entityA) && PlayerLookup.HasComponent(entityB))
        //    {
        //        enemy = entityA;
        //        player = entityB;
        //        return true;
        //    }

        //    if (EnemyLookup.HasComponent(entityB) && PlayerLookup.HasComponent(entityA))
        //    {
        //        enemy = entityB;
        //        player = entityA;
        //        return true;
        //    }

        //    enemy = Entity.Null;
        //    player = Entity.Null;
        //    return false;
        //}


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