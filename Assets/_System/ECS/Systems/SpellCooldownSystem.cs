using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using UnityEditor.Experimental.GraphView;

/// <summary>
/// System that handles spell cooldown and sends request for player or notify if a spell can be casted for enemies.
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
        state.RequireForUpdate<Player>();
        state.RequireForUpdate<Stats>();
        state.RequireForUpdate<ActiveSpell>();
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
        var spellReadyJob = new NotifySpellReadyJob
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
    private partial struct NotifySpellReadyJob : IJobEntity
    {
        [ReadOnly] public BlobAssetReference<SpellBlobs> SpellDatabaseRef;
        public float DeltaTime;
        public EntityCommandBuffer.ParallelWriter ECB;

        void Execute([ChunkIndexInQuery] int chunkIndex, Entity entity,
                     ref DynamicBuffer<ActiveSpell> spells,
                     ref DynamicBuffer<EnemySpellReady> readyBuffer,
                     in Stats stats,
                     in Enemy enemy)
        {

            for (int i = 0; i < spells.Length; i++)
            {
                var spell = spells[i];
                ref readonly var spellData = ref SpellDatabaseRef.Value.Spells[spell.DatabaseIndex];

                if (spell.CooldownTimer > 0)
                    spell.CooldownTimer -= DeltaTime;

                if (spell.CooldownTimer <= 0)
                {
                    readyBuffer.Add(new EnemySpellReady { Caster = entity, Spell = spell });

                    float cooldown = spellData.BaseCooldown * (1 - stats.CooldownReduction);
                    spell.CooldownTimer = cooldown;
                }
                spells[i] = spell;
            }
        }
    }

    [BurstCompile]
    [WithAll(typeof(Stats), typeof(Player))]
    private partial struct CastPlayerSpellJob : IJobEntity
    {
        [ReadOnly] public BlobAssetReference<SpellBlobs> SpellDatabaseRef;
        public float DeltaTime;
        public EntityCommandBuffer.ParallelWriter ECB;

        void Execute([ChunkIndexInQuery] int chunkIndex,
            Entity entity,
            ref DynamicBuffer<ActiveSpell> spells,
            in Stats stats,
            in Player player)
        {
            if (!spells.IsCreated || spells.IsEmpty)
                return;

            for (int i = 0; i < spells.Length; i++)
            {
                var spell = spells[i];
                ref readonly var spellData = ref SpellDatabaseRef.Value.Spells[spell.DatabaseIndex];

                if (spell.CooldownTimer > 0)
                    spell.CooldownTimer -= DeltaTime;

                if (spell.CooldownTimer <= 0)
                {
                    var request = ECB.CreateEntity(chunkIndex);
                    ECB.AddComponent(chunkIndex, request, new CastSpellRequest { Caster = entity, Target = Entity.Null, /*DatabaseRef = spell.DatabaseRef,*/ DatabaseIndex = spell.DatabaseIndex });

                    switch (spellData.ID)
                    {
                        case ESpellID.Fireball:
                            ECB.AddComponent<FireballRequestTag>(chunkIndex, request);
                            break;

                        case ESpellID.LightningStrike:
                            ECB.AddComponent<LightningStrikeRequestTag>(chunkIndex, request);
                            break;

                        case ESpellID.RicochetShot:
                            ECB.AddComponent<RichochetShotRequestTag>(chunkIndex, request);
                        case ESpellID.ThunderStrike:
                            ECB.AddComponent<ThunderStrikeRequestTag>(chunkIndex, request);
                            break;
                    }

                    float cooldown = spellData.BaseCooldown * (1 - stats.CooldownReduction);
                    spell.CooldownTimer = cooldown;
                }

                spells[i] = spell;
            }
        }
    }
}
