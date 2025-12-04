using Unity.Physics.Systems;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Entities;
using Unity.Physics;
using Unity.Burst;

[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
[UpdateAfter(typeof(PhysicsSystemGroup))]
[BurstCompile]
public partial struct CollisionSystem : ISystem
{
    private ComponentLookup<Player> _playerLookup;
    private ComponentLookup<Enemy> _enemyLookup;

    private ComponentLookup<DamageOnContact> _damageOnContactLookup;
    private ComponentLookup<DestroyOnContact> _destroyOnContactLookup;

    private ComponentLookup<LocalTransform> _transformLookup;
    private ComponentLookup<Ricochet> _ricochetLookup;
    private ComponentLookup<Pierce> _pierceLookup;
    private ComponentLookup<LinearMovement> _linearMovementLookup;
    private ComponentLookup<FollowTargetMovement> _followMovementLookup;
    private BufferLookup<HitEntityMemory> _hitMemoryLookup;

    private ComponentLookup<Invincible> _invincibleLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PhysicsStep>();
        state.RequireForUpdate<SimulationSingleton>();
        state.RequireForUpdate<PhysicsWorldSingleton>();
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();

        _playerLookup = state.GetComponentLookup<Player>(true);
        _enemyLookup = state.GetComponentLookup<Enemy>(true);
        _damageOnContactLookup = state.GetComponentLookup<DamageOnContact>(true);
        _destroyOnContactLookup = state.GetComponentLookup<DestroyOnContact>(true);
        _transformLookup = state.GetComponentLookup<LocalTransform>(true);
        _ricochetLookup = state.GetComponentLookup<Ricochet>(false);
        _pierceLookup = state.GetComponentLookup<Pierce>(false);
        _linearMovementLookup = state.GetComponentLookup<LinearMovement>(false);
        _followMovementLookup = state.GetComponentLookup<FollowTargetMovement>(false);
        _hitMemoryLookup = state.GetBufferLookup<HitEntityMemory>(false);
        _invincibleLookup = state.GetComponentLookup<Invincible>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton<GameState>(out var gameState))
            return;

        if (gameState.State != EGameState.Running)
            return;

        _playerLookup.Update(ref state);
        _enemyLookup.Update(ref state);
        _damageOnContactLookup.Update(ref state);
        _destroyOnContactLookup.Update(ref state);
        _transformLookup.Update(ref state);
        _ricochetLookup.Update(ref state);
        _pierceLookup.Update(ref state);
        _linearMovementLookup.Update(ref state);
        _followMovementLookup.Update(ref state);
        _hitMemoryLookup.Update(ref state);
        _invincibleLookup.Update(ref state);

        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        var simulationSingleton = SystemAPI.GetSingleton<SimulationSingleton>();
        var collisionWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld;


        var triggerJob = new TriggerEventJob
        {
            ECB = ecb.AsParallelWriter(),
            CollisionWorld = collisionWorld,

            PlayerLookup = _playerLookup,
            EnemyLookup = _enemyLookup,

            DamageOnContactLookup = _damageOnContactLookup,
            DestroyOnContactLookup = _destroyOnContactLookup,

            LocalTransformLookup = _transformLookup,
            LinearMovementLookup = _linearMovementLookup,
            FollowMovementLookup = _followMovementLookup,

            RicochetLookup = _ricochetLookup,
            PierceLookup = _pierceLookup,
            HitMemoryLookup = _hitMemoryLookup,
            InvincibleLookup = _invincibleLookup
        };

        state.Dependency = triggerJob.Schedule(simulationSingleton, state.Dependency);
    }


    [BurstCompile]
    private struct TriggerEventJob : ITriggerEventsJob
    {
        public EntityCommandBuffer.ParallelWriter ECB;
        [ReadOnly] public CollisionWorld CollisionWorld;

        [ReadOnly] public ComponentLookup<Player> PlayerLookup;
        [ReadOnly] public ComponentLookup<Enemy> EnemyLookup;

        [ReadOnly] public ComponentLookup<DamageOnContact> DamageOnContactLookup;
        [ReadOnly] public ComponentLookup<DestroyOnContact> DestroyOnContactLookup;

        [ReadOnly] public ComponentLookup<LocalTransform> LocalTransformLookup;
        public ComponentLookup<LinearMovement> LinearMovementLookup;
        public ComponentLookup<FollowTargetMovement> FollowMovementLookup;

        public ComponentLookup<Ricochet> RicochetLookup;
        public ComponentLookup<Pierce> PierceLookup;
        public BufferLookup<HitEntityMemory> HitMemoryLookup;
        [ReadOnly] public ComponentLookup<Invincible> InvincibleLookup;

        public void Execute(TriggerEvent triggerEvent)
        {
            Entity entityA = triggerEvent.EntityA;
            Entity entityB = triggerEvent.EntityB;

            // Case Projectile hits target (enemy or player)
            if (TryResolveDamagerVsTarget(entityA, entityB, out Entity damagerEntity, out Entity target))
            {
                // Update Hit Memory Buffer
                if (HitMemoryLookup.HasBuffer(damagerEntity))
                {
                    var history = HitMemoryLookup[damagerEntity];
                    foreach (var hit in history)
                    {
                        // Skip if this target was already hit
                        if (hit.HitEntity == target)
                            return;
                    }
                    // Add target to hit history
                    history.Add(new HitEntityMemory { HitEntity = target });
                }

                // Apply on-hit damages
                bool isInvincible = InvincibleLookup.HasComponent(target);
                if (!isInvincible)
                {
                    var damageData = DamageOnContactLookup[damagerEntity];
                    ECB.AppendToBuffer(0, target, new DamageBufferElement
                    {
                        Damage = damageData.Damage,
                        Element = damageData.Element
                    });
                }

                // Handle cases Ricochet and Piercing before destroying the projectile

                // By default do not destroy the projectile on hit
                bool shouldDestroy = false;

                if (DestroyOnContactLookup.HasComponent(damagerEntity))
                {
                    shouldDestroy = true;
                }

                // Handle Ricochet
                if (RicochetLookup.HasComponent(damagerEntity))
                {
                    var ricochet = RicochetLookup[damagerEntity];
                    if (ricochet.RemainingBounces > 0)
                    {
                        if (TryFindNextTarget(damagerEntity, target, ricochet.BounceRange, out Entity newTarget, out float3 newDirection))
                        {
                            if (LinearMovementLookup.IsComponentEnabled(damagerEntity))
                            {
                                ECB.SetComponentEnabled<LinearMovement>(0, damagerEntity, false);
                                ECB.SetComponentEnabled<FollowTargetMovement>(0, damagerEntity, true);
                            }
                            else if (FollowMovementLookup.IsComponentEnabled(damagerEntity))
                            {
                                // Update projectile movement target
                                var followMove = FollowMovementLookup[damagerEntity];
                                followMove.Target = newTarget;
                                followMove.Speed = math.max(1, ricochet.BounceSpeed);
                                followMove.StopDistance = 0;
                                FollowMovementLookup[damagerEntity] = followMove;
                            }

                            // Decrease remaining bounces
                            ricochet.RemainingBounces--;
                            RicochetLookup[damagerEntity] = ricochet;

                            // Create camera shake feedback request
                            var feedbackReqEntity = ECB.CreateEntity(0);
                            ECB.AddComponent<ShakeFeedbackRequest>(0, feedbackReqEntity);

                            // Do not destroy the projectile
                            shouldDestroy = false;
                        }
                    }
                    //else
                    //{
                    //    shouldDestroy = true;
                    //}
                }

                // Handle Piercing
                // Apply Piercing logic only if not Ricochet
                else if (PierceLookup.HasComponent(damagerEntity))
                {
                    var pierce = PierceLookup[damagerEntity];
                    if (pierce.RemainingPierces > 0)
                    {
                        // Decrease remaining pierces
                        pierce.RemainingPierces--;
                        PierceLookup[damagerEntity] = pierce;

                        // Do not destroy the projectile
                        shouldDestroy = false;
                    }
                    //else
                    //{
                    //    shouldDestroy = true;
                    //}
                }

                if (shouldDestroy)
                {
                    ECB.AddComponent(0, damagerEntity, new DestroyEntityFlag());

                    // If needed, create AoE damage effect on projectile destroyed (ExplosionLookup etc)
                    // If needed, create feedback request 
                }
            }
        }

        /// <summary>
        /// Try to resolve which entity is the damager and which is the target (enemy or player).
        /// </summary>
        /// <param name="entityA"></param>
        /// <param name="entityB"></param>
        /// <param name="damager"></param>
        /// <param name="target"></param>
        /// <param name="targetIsPlayer"></param>
        /// <returns></returns>
        private bool TryResolveDamagerVsTarget(Entity entityA, Entity entityB, out Entity damager, out Entity target, bool targetIsPlayer = false)
        {
            if (DamageOnContactLookup.HasComponent(entityA) && (targetIsPlayer ? PlayerLookup.HasComponent(entityB) : EnemyLookup.HasComponent(entityB)))
            {
                damager = entityA;
                target = entityB;
                return true;
            }

            if (DamageOnContactLookup.HasComponent(entityB) && (targetIsPlayer ? PlayerLookup.HasComponent(entityA) : EnemyLookup.HasComponent(entityA)))
            {
                damager = entityB;
                target = entityA;
                return true;
            }

            damager = Entity.Null;
            target = Entity.Null;
            return false;
        }

        /// <summary>
        /// Try to find the next target for a ricochet spell.
        /// </summary>
        /// <param name="ricochetEntity"></param>
        /// <param name="currentTarget"></param>
        /// <param name="range"></param>
        /// <param name="direction"></param>
        /// <returns></returns>
        private bool TryFindNextTarget(Entity ricochetEntity, Entity currentTarget, float range, out Entity newTarget, out float3 direction)
        {
            var currentPos = LocalTransformLookup[ricochetEntity].Position;
            var hits = new NativeList<DistanceHit>(Allocator.TempJob);

            // @todo set proper collision layers based on caster
            // Set filter only for enemies
            var filter = new CollisionFilter
            {
                BelongsTo = CollisionLayers.Raycast,
                CollidesWith = CollisionLayers.Enemy
            };

            CollisionWorld.OverlapSphere(currentPos, range, ref hits, filter);

            float closestDist = float.MaxValue;
            float3 bestTargetPos = float3.zero;
            bool found = false;
            newTarget = Entity.Null;

            foreach (var hit in hits)
            {
                // Skip current target and self
                if (hit.Entity == currentTarget || hit.Entity == ricochetEntity)
                    continue;

                // Ensure it's an enemy
                if (!EnemyLookup.HasComponent(hit.Entity))
                    continue;

                if (hit.Distance < closestDist)
                {
                    closestDist = hit.Distance;
                    bestTargetPos = hit.Position;
                    found = true;
                    newTarget = hit.Entity;
                }
            }

            if (found)
            {
                direction = math.normalize(bestTargetPos - currentPos);
                direction = math.normalize(direction);
            }
            else
            {
                direction = float3.zero;
            }

            hits.Dispose();

            return found;
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