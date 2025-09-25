using Unity.Collections;
using Unity.Entities;

/// <summary>
/// System that handles spell cooldown and sends request or notify if a spell can be casted.
/// <para>
/// Player will send request to be handle by spells systems.
/// </para>
/// <para>
/// Enemies will notify spell ready to be handle by <see cref="EnemyTargetingSystem"/>.
/// </para>
/// <para>@todo AI system to decide which spell to cast or how to move.</para> 
/// </summary>
public partial struct SpellCasterSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Player>();
        state.RequireForUpdate<Stats>();
        state.RequireForUpdate<ActiveSpell>();
    }

    public void OnUpdate(ref SystemState state)
    {
        var deltaTime = SystemAPI.Time.DeltaTime;
        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        EntityCommandBuffer ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        ComponentLookup<Stats> statsLookup = SystemAPI.GetComponentLookup<Stats>(true);

        var spellCasterJob = new SpellCasterJob
        {
            DeltaTime = deltaTime,
            ECB = ecb.AsParallelWriter(),
            StatsLookup = statsLookup
        };
        var spellCasterHandle = spellCasterJob.ScheduleParallel(state.Dependency);
        spellCasterHandle.Complete();

        var spellReadyJob = new SpellReadyNotifyJob
        {
            DeltaTime = deltaTime,
            ECB = ecb.AsParallelWriter(),
            StatsLookup = statsLookup,
            //EnemySpellReadyLookup = SystemAPI.GetBufferLookup<EnemySpellReady>(false)
        };
        var spellReadyHandle = spellReadyJob.ScheduleParallel(state.Dependency);
        spellReadyHandle.Complete();
    }

    [WithAll(typeof(Stats), typeof(Enemy))]
    private partial struct SpellReadyNotifyJob : IJobEntity
    {
        public float DeltaTime;

        public EntityCommandBuffer.ParallelWriter ECB;

        //public BufferLookup<EnemySpellReady> EnemySpellReadyLookup;

        [ReadOnly] public ComponentLookup<Stats> StatsLookup;

        void Execute([ChunkIndexInQuery] int chunkIndex, Entity entity, ref DynamicBuffer<ActiveSpell> spells, ref DynamicBuffer<EnemySpellReady> readyBuffer, in Enemy enemy)
        {
            //var spellReadyBuffer = EnemySpellReadyLookup[entity];

            var stats = StatsLookup[entity];

            for (int i = 0; i < spells.Length; i++)
            {
                var spell = spells[i];
                if (spell.CooldownTimer > 0) spell.CooldownTimer -= DeltaTime;

                if (spell.CooldownTimer <= 0)
                {
                    //spellReadyBuffer.Add(new EnemySpellReady { Caster = entity, SpellID = spell.ID });
                    readyBuffer.Add(new EnemySpellReady { Caster = entity, Spell = spell });

                    float cooldown = spell.BaseCooldown * (1 - stats.CooldownReduction);
                    spell.CooldownTimer = cooldown;
                }
                spells[i] = spell;
            }
        }
    }

    [WithAll(typeof(Stats), typeof(Player))]
    private partial struct SpellCasterJob : IJobEntity
    {
        public float DeltaTime;

        public EntityCommandBuffer.ParallelWriter ECB;

        [ReadOnly] public ComponentLookup<Stats> StatsLookup;

        void Execute([ChunkIndexInQuery] int chunkIndex, Entity entity, ref DynamicBuffer<ActiveSpell> spells, in Player player)
        {
            if (!spells.IsCreated || spells.IsEmpty)
                return;

            var stats = StatsLookup[entity];

            for (int i = 0; i < spells.Length; i++)
            {
                ActiveSpell spell = spells[i];

                if (spell.CooldownTimer > 0)
                    spell.CooldownTimer -= DeltaTime;

                if (spell.CooldownTimer <= 0)
                {
                    var request = ECB.CreateEntity(chunkIndex);
                    ECB.AddComponent(chunkIndex, request, new CastSpellRequest { Caster = entity, SpellID = spell.ID });

                    float cooldown = spell.BaseCooldown * (1 - stats.CooldownReduction);
                    spell.CooldownTimer = cooldown;
                }

                spells[i] = spell;
            }
        }


    }
}
