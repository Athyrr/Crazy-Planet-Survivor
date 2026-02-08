using Unity.Collections;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Entities;
using Unity.Physics;
using Unity.Burst;


[UpdateInGroup(typeof(SimulationSystemGroup))]
[BurstCompile]
public partial struct AuraDamageSystem : ISystem
{
    private ComponentLookup<Player> _playerLookup;
    private ComponentLookup<Enemy> _enemyLookup;
    private ComponentLookup<Stats> _statsLookup;
    private BufferLookup<DamageBufferElement> _damageBufferLookup;
    private ComponentLookup<DestroyEntityFlag> _destroyFLagLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PhysicsWorldSingleton>();

        // Cache lookups
        _playerLookup = state.GetComponentLookup<Player>(true);
        _enemyLookup = state.GetComponentLookup<Enemy>(true);
        _statsLookup = state.GetComponentLookup<Stats>(true);
        _damageBufferLookup = state.GetBufferLookup<DamageBufferElement>(true);
        _destroyFLagLookup = state.GetComponentLookup<DestroyEntityFlag>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // Get game state
        if (!SystemAPI.TryGetSingleton<GameState>(out var gameState))
            return;

        // Only run when game is running
        if (gameState.State != EGameState.Running)
            return;

        // Setup ECB    
        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        // Get collision world
        var collisionWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld;
        //var collisionWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().PhysicsWorld;

        // Update lookups
        _playerLookup.Update(ref state);
        _enemyLookup.Update(ref state);
        _statsLookup.Update(ref state);
        _damageBufferLookup.Update(ref state);
        _destroyFLagLookup.Update(ref state);

        var auraTickJob = new AuraTickJob
        {
            ECB = ecb.AsParallelWriter(),
            DeltaTime = SystemAPI.Time.DeltaTime,

            CollisionWorld = collisionWorld,

            PlayerLookup = _playerLookup,
            EnemyLookup = _enemyLookup,

            StatsLookup = _statsLookup,
            DamageBufferLookup = _damageBufferLookup,
            DestroyFLagLookup = _destroyFLagLookup
        };

        state.Dependency = auraTickJob.ScheduleParallel(state.Dependency);
    }

    /// <summary>
    /// @todo summary for all jobs in game
    /// </summary>
    [BurstCompile]
    private partial struct AuraTickJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter ECB;
        public float DeltaTime;

        [ReadOnly] public CollisionWorld CollisionWorld;

        [ReadOnly] public ComponentLookup<Player> PlayerLookup;
        [ReadOnly] public ComponentLookup<Enemy> EnemyLookup;

        [ReadOnly] public ComponentLookup<Stats> StatsLookup;
        [ReadOnly] public BufferLookup<DamageBufferElement> DamageBufferLookup;
        [ReadOnly] public ComponentLookup<DestroyEntityFlag> DestroyFLagLookup;

        public void Execute([ChunkIndexInQuery] int chunkIndex, Entity auraSpellEntity, ref DamageOnTick damageOnTick, in LocalToWorld worldPositionMatrix)
        {
            damageOnTick.ElapsedTime += DeltaTime;

            // Wait for next tick
            if (damageOnTick.ElapsedTime < damageOnTick.TickRate)
                return;

            damageOnTick.ElapsedTime = 0f;

            var caster = damageOnTick.Caster;
            var casterStats = StatsLookup[caster];

            var hits = new NativeList<DistanceHit>(Allocator.Temp);

            // Set detection filter
            bool isPlayerCaster = PlayerLookup.HasComponent(caster);
            CollisionFilter filter = new CollisionFilter
            {
                BelongsTo = isPlayerCaster ? CollisionLayers.PlayerSpell : CollisionLayers.EnemySpell,
                CollidesWith = (isPlayerCaster ? CollisionLayers.Enemy : CollisionLayers.Player) /*| CollisionLayers.Obstacle*/,
            };

            float radius = damageOnTick.AreaRadius * math.max(1, casterStats.EffectAreaRadiusMult);
            float damage = damageOnTick.DamagePerTick /** math.max(1, casterStats.DamageMult)*/;

            // Detection
            CollisionWorld.OverlapSphere(worldPositionMatrix.Position, radius, ref hits, filter);

            foreach (var hit in hits)
            {
                var isEnemyHit = EnemyLookup.HasComponent(hit.Entity);

                // Ignore entities that are neither player nor enemy
                if (!isEnemyHit && !PlayerLookup.HasComponent(hit.Entity))
                    continue;

                // Ignore entities that cannot receive damage
                if (!DamageBufferLookup.HasBuffer(hit.Entity))
                    continue;

                // Ignore destroyed entities
                if (DestroyFLagLookup.HasComponent(hit.Entity))
                    continue;

                ECB.AppendToBuffer(chunkIndex, hit.Entity, new DamageBufferElement
                {
                    Damage = damage,
                    Element = damageOnTick.Element,
                });

                // Shake feedback
                var feedbackReqEntity = ECB.CreateEntity(chunkIndex);
                ECB.AddComponent<ShakeFeedbackRequest>(chunkIndex, feedbackReqEntity);
            }

            hits.Dispose();
        }
    }
}
