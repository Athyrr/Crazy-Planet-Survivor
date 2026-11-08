using Unity.Collections;
using Unity.Entities;
using Unity.Burst;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

/// <summary>
/// System that handles spell cooldown and sends request for player or notify if a spell can be casted for enemies.
/// personne ne lit les commits du coup je peut dire que c'est tres mal coder 
/// <para>
/// Player will send request to be handle by spells systems.
/// </para>
/// <para>
/// Enemies will be notified with a spell ready comp to be handled by <see cref="EnemyTargetingSystem"/>.
/// </para>
/// <para>@todo AI system to decide which spell to cast or how to move.</para> 
/// </summary>
[BurstCompile]
public partial struct SpellCooldownSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        state.RequireForUpdate<CoreStats>();
        state.RequireForUpdate<Player>();
        state.RequireForUpdate<ActiveSpell>();
        state.RequireForUpdate<SpellsDatabase>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton<GameState>(out var gameState))
            return;

        if (gameState.State != EGameState.Running)
            return;

        var deltaTime = SystemAPI.Time.DeltaTime;
        var spellDatabase = SystemAPI.GetSingleton<SpellsDatabase>();

        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecbPlayer = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
        var ecbEnemy = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
        
        // Player spell casting job
        var spellCasterJob = new CastPlayerSpellJob
        {
            ECB = ecbPlayer.AsParallelWriter(),
            DeltaTime = deltaTime,
            SpellDatabaseRef = spellDatabase.Blobs,
        };
        JobHandle spellCasterHandle = spellCasterJob.ScheduleParallel(state.Dependency);

        // Enemy spell ready notification job
        var spellReadyJob = new NotifyEnemySpellReadyJob
        {
            ECB = ecbEnemy.AsParallelWriter(),
            DeltaTime = deltaTime,
            SpellDatabaseRef = spellDatabase.Blobs,
        };
        JobHandle spellReadyHandle = spellReadyJob.ScheduleParallel(spellCasterHandle);

        // Summon spell casting job (spec allegeance-summon §3): own ECB (one per producer job), chained after the two others.
        JobHandle summonHandle = spellReadyHandle;
        if (SystemAPI.TryGetSingleton<PhysicsWorldSingleton>(out var physics))
        {
            var ecbSummon = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
            summonHandle = new CastSummonSpellJob
            {
                ECB = ecbSummon.AsParallelWriter(),
                DeltaTime = deltaTime,
                SpellDatabaseRef = spellDatabase.Blobs,
                CollisionWorld = physics.CollisionWorld,
            }.ScheduleParallel(spellReadyHandle);
        }

        state.Dependency = summonHandle;
    }

    [BurstCompile]
    [WithAll(typeof(Enemy))]
    private partial struct NotifyEnemySpellReadyJob : IJobEntity
    {
        [ReadOnly] public BlobAssetReference<SpellBlobs> SpellDatabaseRef;
        public float DeltaTime;
        public EntityCommandBuffer.ParallelWriter ECB;

        void Execute(
            [ChunkIndexInQuery] int chunkIndex,
            Entity entity,
            ref DynamicBuffer<ActiveSpell> spells,
            ref DynamicBuffer<EnemySpellReady> readyBuffer)
        {
            if (!spells.IsCreated || spells.IsEmpty)
                return;

            ref var spellBlobs = ref SpellDatabaseRef.Value.Spells;

            for (int i = 0; i < spells.Length; i++)
            {
                ActiveSpell activeSpell = spells[i];
                ref var spellData = ref spellBlobs[activeSpell.DatabaseIndex];

                // If passive or one shot spell
                if (spellData.BaseCooldown <= 0)
                    continue;

                if (activeSpell.CurrentCooldown > 0)
                    activeSpell.CurrentCooldown -= DeltaTime;

                if (activeSpell.CurrentCooldown <= 0)
                {
                    readyBuffer.Add(new EnemySpellReady
                    {
                        Caster = entity,
                        Spell = activeSpell
                    });

                    // activeSpell.CurrentCooldown = activeSpell.FinalCooldown;
                }

                spells[i] = activeSpell;
            }
        }
    }

    [BurstCompile]
    [WithAll(typeof(Player))]
    private partial struct CastPlayerSpellJob : IJobEntity
    {
        [ReadOnly] public float DeltaTime;
        public EntityCommandBuffer.ParallelWriter ECB;

        [ReadOnly] public BlobAssetReference<SpellBlobs> SpellDatabaseRef;

        void Execute([ChunkIndexInQuery] int chunkIndex, Entity entity, ref DynamicBuffer<ActiveSpell> spells)
        {
            if (!spells.IsCreated || spells.IsEmpty)
                return;

            ref var spellBlobs = ref SpellDatabaseRef.Value.Spells;

            for (int i = 0; i < spells.Length; i++)
            {
                var activeSpell = spells[i];
                ref var spellData = ref spellBlobs[activeSpell.DatabaseIndex];

                if (spellData.BaseCooldown <= 0)
                    continue;

                if (activeSpell.CurrentCooldown > 0)
                    activeSpell.CurrentCooldown -= DeltaTime;

                if (activeSpell.CurrentCooldown <= 0)
                {
                    // todo Send reuqest on caster entity not create a request entity
                    var request = ECB.CreateEntity(chunkIndex);
                    ECB.AddComponent(chunkIndex, request, new CastSpellRequest
                    {
                        Caster = entity,
                        Target = Entity.Null,
                        DatabaseIndex = activeSpell.DatabaseIndex
                    });

                    activeSpell.CurrentCooldown = activeSpell.FinalCooldown;
                }

                spells[i] = activeSpell;
            }
        }
    }

    /// <summary>Summon agents: same cooldown → CastSpellRequest model as the player, but only while a hostile is within
    /// the spell's FinalRange (Targeting Nearest); otherwise the spell stays ready and fires as soon as one comes in.</summary>
    [BurstCompile]
    [WithAll(typeof(Summon))]
    private partial struct CastSummonSpellJob : IJobEntity
    {
        [ReadOnly] public float DeltaTime;
        public EntityCommandBuffer.ParallelWriter ECB;
        [ReadOnly] public BlobAssetReference<SpellBlobs> SpellDatabaseRef;
        [ReadOnly] public CollisionWorld CollisionWorld;

        void Execute([ChunkIndexInQuery] int chunkIndex, Entity entity, in LocalTransform transform, in Team team,
            ref DynamicBuffer<ActiveSpell> spells)
        {
            if (spells.IsEmpty)
                return;

            ref var spellBlobs = ref SpellDatabaseRef.Value.Spells;
            uint hostile = TeamUtils.HostileLayers(team.Value);

            for (int i = 0; i < spells.Length; i++)
            {
                var activeSpell = spells[i];
                if (spellBlobs[activeSpell.DatabaseIndex].BaseCooldown <= 0)
                    continue;

                if (activeSpell.CurrentCooldown > 0)
                    activeSpell.CurrentCooldown -= DeltaTime;

                // A brand-new agent's attack spell is not computed yet (Final* = 0, SpellActivationSystem): FinalRange 0
                // makes HasHostileInRange false, so it never fires uncomputed and stays ready for the next frame
                // (a computed spell has FinalRange >= 1, SpellStatsCalculationSystem).
                if (activeSpell.CurrentCooldown <= 0 && HasHostileInRange(transform.Position, activeSpell.FinalRange, hostile))
                {
                    var request = ECB.CreateEntity(chunkIndex);
                    ECB.AddComponent(chunkIndex, request, new CastSpellRequest
                    {
                        Caster = entity,
                        Target = Entity.Null,
                        DatabaseIndex = activeSpell.DatabaseIndex
                    });
                    activeSpell.CurrentCooldown = activeSpell.FinalCooldown;
                }

                spells[i] = activeSpell;
            }
        }

        private bool HasHostileInRange(float3 position, float range, uint hostileLayers)
        {
            if (range <= 0f || hostileLayers == 0u)
                return false;
            var input = new PointDistanceInput
            {
                Position = position,
                MaxDistance = range,
                Filter = new CollisionFilter { BelongsTo = CollisionLayers.Raycast, CollidesWith = hostileLayers },
            };
            return CollisionWorld.CalculateDistance(input, out DistanceHit _);
        }
    }
}