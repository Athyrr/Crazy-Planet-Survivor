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

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PhysicsWorldSingleton>();

        _playerLookup = state.GetComponentLookup<Player>(true);
        _enemyLookup = state.GetComponentLookup<Enemy>(true);
        _statsLookup = state.GetComponentLookup<Stats>(true);
        _damageBufferLookup = state.GetBufferLookup<DamageBufferElement>(true);
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

        var physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().PhysicsWorld;

        _playerLookup.Update(ref state);
        _enemyLookup.Update(ref state);
        _statsLookup.Update(ref state);
        _damageBufferLookup.Update(ref state);

        var auraTickJob = new AuraTickJob
        {
            ECB = ecb.AsParallelWriter(),
            DeltaTime = SystemAPI.Time.DeltaTime,

            PhysicsWorld = physicsWorld,

            PlayerLookup = _playerLookup,
            EnemyLookup = _enemyLookup,
            StatsLookup = _statsLookup,
            DamageBufferLookup = _damageBufferLookup,
        };

        state.Dependency = auraTickJob.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    private partial struct AuraTickJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter ECB;
        public float DeltaTime;

        [ReadOnly] public PhysicsWorld PhysicsWorld;

        [ReadOnly] public ComponentLookup<Player> PlayerLookup;
        [ReadOnly] public ComponentLookup<Enemy> EnemyLookup;
        [ReadOnly] public ComponentLookup<Stats> StatsLookup;
        [ReadOnly] public BufferLookup<DamageBufferElement> DamageBufferLookup;

        public void Execute([ChunkIndexInQuery] int chunkIndex, Entity auraSpellEntity, ref DamageOnTick damageOnTick, in LocalToWorld worldPositionMatrix)
        {
            damageOnTick.ElapsedTime += DeltaTime;

            if (damageOnTick.ElapsedTime < damageOnTick.TickRate)
                return;

            damageOnTick.ElapsedTime = 0f;

            var caster = damageOnTick.Caster;
            var casterStats = StatsLookup[caster];

            var hits = new NativeList<DistanceHit>(Allocator.Temp);

            bool isPlayerCaster = PlayerLookup.HasComponent(caster);
            CollisionFilter filter = new CollisionFilter
            {
                BelongsTo = isPlayerCaster ? CollisionLayers.PlayerSpell : CollisionLayers.EnemySpell,
                CollidesWith = (isPlayerCaster ? CollisionLayers.Enemy : CollisionLayers.Player) /*| CollisionLayers.Obstacle*/,
            };

            float radius = damageOnTick.AreaRadius * math.max(1, casterStats.EffectAreaRadiusMult);
            float damage = damageOnTick.DamagePerTick /** math.max(1, casterStats.DamageMult)*/;

            PhysicsWorld.OverlapSphere(worldPositionMatrix.Position, radius, ref hits, filter);

            foreach (var hit in hits)
            {
                if (!EnemyLookup.HasComponent(hit.Entity) && !PlayerLookup.HasComponent(hit.Entity))
                    continue;

                if (DamageBufferLookup.HasBuffer(hit.Entity))
                {
                    ECB.AppendToBuffer(chunkIndex, hit.Entity, new DamageBufferElement
                    {
                        Damage = damage,
                        Element = damageOnTick.Element,
                    });
                }
            }

            hits.Dispose();
        }
    }
}
