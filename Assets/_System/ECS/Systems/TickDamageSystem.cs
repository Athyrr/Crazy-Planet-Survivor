using Unity.Collections;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Entities;
using Unity.Physics;
using Unity.Burst;
using Unity.Jobs;

/// <summary>
/// System that handle damages on tick and not on collisions.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[BurstCompile]
public partial struct TickDamageSystem : ISystem
{
    private ComponentLookup<Player> _playerLookup;
    private ComponentLookup<Enemy> _enemyLookup;
    private ComponentLookup<Stats> _statsLookup;
    private BufferLookup<DamageBufferElement> _damageBufferLookup;
    private ComponentLookup<DestroyEntityFlag> _destroyFLagLookup;
    private BufferLookup<ActiveSpell> _activeSpellBufferLookup;

    private EntityQuery _playerQuery;
    private NativeQueue<SpellDamageEvent> _damageEventsQueue;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PhysicsWorldSingleton>();
        state.RequireForUpdate<Player>();

        _playerQuery = state.GetEntityQuery(ComponentType.ReadOnly<Player>());

        // Cache lookups
        _playerLookup = state.GetComponentLookup<Player>(true);
        _enemyLookup = state.GetComponentLookup<Enemy>(true);
        _statsLookup = state.GetComponentLookup<Stats>(true);
        _damageBufferLookup = state.GetBufferLookup<DamageBufferElement>(true);
        _destroyFLagLookup = state.GetComponentLookup<DestroyEntityFlag>(true);
        _activeSpellBufferLookup = state.GetBufferLookup<ActiveSpell>(false);
        
        _damageEventsQueue = new NativeQueue<SpellDamageEvent>(Allocator.Persistent);
    }

    public void OnDestroy(ref SystemState state)
    {
        if (_damageEventsQueue.IsCreated)
            _damageEventsQueue.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // Get game state
        if (!SystemAPI.TryGetSingleton<GameState>(out var gameState))
            return;

        if (_playerQuery.IsEmpty)
            return;

        // Only run when game is running
        if (gameState.State != EGameState.Running)
            return;

        // Setup ECB    
        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        // Get collision world
        var collisionWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld;

        // Update lookups
        _playerLookup.Update(ref state);
        _enemyLookup.Update(ref state);
        _statsLookup.Update(ref state);
        _damageBufferLookup.Update(ref state);
        _destroyFLagLookup.Update(ref state);
        _activeSpellBufferLookup.Update(ref state);

        var auraTickJob = new TickDamageJob
        {
            ECB = ecb.AsParallelWriter(),
            DeltaTime = SystemAPI.Time.DeltaTime,

            CollisionWorld = collisionWorld,

            PlayerLookup = _playerLookup,
            EnemyLookup = _enemyLookup,

            StatsLookup = _statsLookup,
            DamageBufferLookup = _damageBufferLookup,
            DestroyFLagLookup = _destroyFLagLookup,
            
            DamageEventsWriter = _damageEventsQueue.AsParallelWriter()
        };
        JobHandle tickHandle = auraTickJob.ScheduleParallel(state.Dependency);

        var trackJob = new TrackTickDamageJob
        {
            DamageEventsQueue = _damageEventsQueue,
            ActiveSpellLookup = _activeSpellBufferLookup,
            PlayerEntity = SystemAPI.GetSingletonEntity<Player>()
        };

        state.Dependency = trackJob.Schedule(tickHandle);
    }

    /// <summary>
    /// @todo summary for all jobs in game
    /// </summary>
    [BurstCompile]
    private partial struct TickDamageJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter ECB;
        public float DeltaTime;

        [ReadOnly] public CollisionWorld CollisionWorld;

        [ReadOnly] public ComponentLookup<Player> PlayerLookup;
        [ReadOnly] public ComponentLookup<Enemy> EnemyLookup;

        [ReadOnly] public ComponentLookup<Stats> StatsLookup;
        [ReadOnly] public BufferLookup<DamageBufferElement> DamageBufferLookup;
        [ReadOnly] public ComponentLookup<DestroyEntityFlag> DestroyFLagLookup;

        public NativeQueue<SpellDamageEvent>.ParallelWriter DamageEventsWriter;

        public void Execute([ChunkIndexInQuery] int chunkIndex, Entity auraSpellEntity, ref DamageOnTick damageOnTick,
            in LocalToWorld worldPosition, in SpellSource spellSource)
        {
            damageOnTick.ElapsedTime += DeltaTime;

            // Wait for next tick
            if (damageOnTick.ElapsedTime < damageOnTick.TickRate)
                return;

            damageOnTick.ElapsedTime = 0f;
            var caster = damageOnTick.Caster;

            if (!StatsLookup.HasComponent(caster))
                return;
            var casterStats = StatsLookup[caster];

            var hits = new NativeList<DistanceHit>(Allocator.Temp);

            // Set detection filter
            bool isPlayerCaster = PlayerLookup.HasComponent(caster);
            CollisionFilter filter = new CollisionFilter
            {
                BelongsTo = isPlayerCaster ? CollisionLayers.PlayerSpell : CollisionLayers.EnemySpell,
                CollidesWith =
                    (isPlayerCaster ? CollisionLayers.Enemy : CollisionLayers.Player) /*| CollisionLayers.Obstacle*/,
            };

            float radius = damageOnTick.AreaRadius * math.max(1, casterStats.AreaOfEffectMult);
            float damage = damageOnTick.DamagePerTick; // todo scale tick damage based on stats

            // Detection
            CollisionWorld.OverlapSphere(worldPosition.Position, radius, ref hits, filter);

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
                    Damage = (int)damage,
                    Element = damageOnTick.Element,
                });

                DamageEventsWriter.Enqueue(new SpellDamageEvent
                {
                    DatabaseIndex = spellSource.DatabaseIndex,
                    DamageAmount = (int)damage
                });

                // todo feedbacks damage and hitframe
                // Shake feedback
                var feedbackReqEntity = ECB.CreateEntity(chunkIndex);
                ECB.AddComponent<ShakeFeedbackRequest>(chunkIndex, feedbackReqEntity);
            }

            hits.Dispose();
        }
    }

    [BurstCompile]
    private struct TrackTickDamageJob : IJob
    {
        public NativeQueue<SpellDamageEvent> DamageEventsQueue;
        public BufferLookup<ActiveSpell> ActiveSpellLookup;
        public Entity PlayerEntity;

        public void Execute()
        {
            var sums = new NativeHashMap<int, int>(16, Allocator.Temp);
            while (DamageEventsQueue.TryDequeue(out var evt))
            {
                if (sums.ContainsKey(evt.DatabaseIndex)) 
                    sums[evt.DatabaseIndex] += evt.DamageAmount;
                else
                    sums.Add(evt.DatabaseIndex, evt.DamageAmount);
            }

            if (ActiveSpellLookup.TryGetBuffer(PlayerEntity, out var buffer))
            {
                for (int i = 0; i < buffer.Length; i++)
                {
                    var spell = buffer[i];
                    if (sums.TryGetValue(spell.DatabaseIndex, out int totalAdded))
                    {
                        spell.DamageDealt += totalAdded;
                        buffer[i] = spell;
                    }
                }
            }

            sums.Dispose();
        }
    }
}