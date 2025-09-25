using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using UnityEngine;

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
        var deltaTime = SystemAPI.Time.DeltaTime;
        EndSimulationEntityCommandBufferSystem.Singleton ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();

        EntityCommandBuffer ecbPlayer = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
        var spellCasterJob = new SpellCasterJob
        {
            DeltaTime = deltaTime,
            ECB = ecbPlayer.AsParallelWriter(),
        };
        JobHandle spellCasterHandle = spellCasterJob.ScheduleParallel(state.Dependency);

        EntityCommandBuffer ecbEnemy = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
        var spellReadyJob = new SpellReadyNotifyJob
        {
            DeltaTime = deltaTime,
            ECB = ecbEnemy.AsParallelWriter(),
        };
        JobHandle spellReadyHandle = spellReadyJob.ScheduleParallel(spellCasterHandle);

        state.Dependency = spellReadyHandle;
    }



    [BurstCompile]
    [WithAll(typeof(Stats), typeof(Enemy))]
    private partial struct SpellReadyNotifyJob : IJobEntity
    {
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
                if (spell.CooldownTimer > 0) spell.CooldownTimer -= DeltaTime;

                if (spell.CooldownTimer <= 0)
                {
                    readyBuffer.Add(new EnemySpellReady { Caster = entity, Spell = spell });

                    float cooldown = spell.BaseCooldown * (1 - stats.CooldownReduction);
                    spell.CooldownTimer = cooldown;
                }
                spells[i] = spell;
            }
        }
    }

    [WithAll(typeof(Stats), typeof(Player))]
    [BurstCompile]
    private partial struct SpellCasterJob : IJobEntity
    {
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
