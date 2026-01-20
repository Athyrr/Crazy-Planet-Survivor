using Unity.Collections;
using Unity.Entities;
using Unity.Burst;
using Unity.Jobs;

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
        state.RequireForUpdate<Stats>();
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

        EndSimulationEntityCommandBufferSystem.Singleton ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        EntityCommandBuffer ecbPlayer = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        var spellCasterJob = new CastPlayerSpellJob
        {
            DeltaTime = deltaTime,
            SpellDatabaseRef = spellDatabase.Blobs,
            ECB = ecbPlayer.AsParallelWriter(),
        };
        JobHandle spellCasterHandle = spellCasterJob.ScheduleParallel(state.Dependency);

        EntityCommandBuffer ecbEnemy = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
        var spellReadyJob = new NotifyEnemySpellReadyJob
        {
            DeltaTime = deltaTime,
            SpellDatabaseRef = spellDatabase.Blobs,
            ECB = ecbEnemy.AsParallelWriter(),
        };
        JobHandle spellReadyHandle = spellReadyJob.ScheduleParallel(spellCasterHandle);

        state.Dependency = spellReadyHandle;
    }



    [BurstCompile]
    [WithAll(typeof(Stats), typeof(Enemy))]
    private partial struct NotifyEnemySpellReadyJob : IJobEntity
    {
        [ReadOnly] public BlobAssetReference<SpellBlobs> SpellDatabaseRef;
        public float DeltaTime;
        public EntityCommandBuffer.ParallelWriter ECB;

        void Execute([ChunkIndexInQuery] int chunkIndex, Entity entity, ref DynamicBuffer<ActiveSpell> spells, ref DynamicBuffer<EnemySpellReady> readyBuffer, in Stats stats, in Enemy enemy)
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

                    float cdMult = activeSpell.CooldownMultiplier <= 0 ? 1f : activeSpell.CooldownMultiplier;
                    float cooldown = spellData.BaseCooldown * cdMult * (1f - stats.CooldownReduction);

                    activeSpell.CurrentCooldown = cooldown;
                }
                spells[i] = activeSpell;
            }
        }
    }

    [BurstCompile]
    [WithAll(typeof(Stats), typeof(Player))]
    private partial struct CastPlayerSpellJob : IJobEntity
    {
        [ReadOnly] public float DeltaTime;
        public EntityCommandBuffer.ParallelWriter ECB;

        [ReadOnly] public BlobAssetReference<SpellBlobs> SpellDatabaseRef;

        void Execute([ChunkIndexInQuery] int chunkIndex, Entity entity, ref DynamicBuffer<ActiveSpell> spells, in Stats stats, in Player player)
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
                    var request = ECB.CreateEntity(chunkIndex);
                    ECB.AddComponent(chunkIndex, request, new CastSpellRequest
                    {
                        Caster = entity,
                        Target = Entity.Null,
                        DatabaseIndex = activeSpell.DatabaseIndex
                    });

                    float cooldownMult = activeSpell.CooldownMultiplier <= 0 ? 1f : activeSpell.CooldownMultiplier;
                    float cooldown = spellData.BaseCooldown * cooldownMult * (1f - stats.CooldownReduction);

                    activeSpell.CurrentCooldown = cooldown;
                }

                spells[i] = activeSpell;
            }
        }
    }
}
