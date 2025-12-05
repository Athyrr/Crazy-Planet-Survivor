using Unity.Collections;
using Unity.Entities;
using Unity.Burst;

/// <summary>
/// System that handles activation of spells when requested. Activate Initial spells for entities then when a new spell is unlocked..
/// </summary>
[UpdateInGroup(typeof(InitializationSystemGroup))]
[BurstCompile]
public partial struct SpellActivationSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<SpellsDatabase>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // Create entity for Spell to Index Mapper
        // Done in update, ensure that SpellToIndexMap is baked well
        if (!SystemAPI.HasSingleton<SpellToIndexMap>())
        {
            SpellsDatabase db = SystemAPI.GetSingleton<SpellsDatabase>();
            ref var spellsDatabase = ref db.Blobs.Value.Spells;

            NativeHashMap<SpellKey, int> map = new NativeHashMap<SpellKey, int>(spellsDatabase.Length, Allocator.Persistent);
            for (int i = 0; i < spellsDatabase.Length; i++)
            {
                map.TryAdd(new SpellKey { Value = spellsDatabase[i].ID }, i);
            }

            var mapEntity = state.EntityManager.CreateEntity();
            state.EntityManager.AddComponentData(mapEntity, new SpellToIndexMap { Map = map });
        }

        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        var spellIndexMap = SystemAPI.GetSingleton<SpellToIndexMap>().Map;
        var database = SystemAPI.GetSingleton<SpellsDatabase>();

        var activateSpellJob = new ActivateSpellJob()
        {
            ECB = ecb.AsParallelWriter(),

            SpellIndexMap = spellIndexMap,
            SpellsDatabaseRef = database.Blobs
        };
        state.Dependency = activateSpellJob.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    [WithAll(typeof(SpellActivationRequest))]
    private partial struct ActivateSpellJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter ECB;

        [ReadOnly] public NativeHashMap<SpellKey, int> SpellIndexMap;
        [ReadOnly] public BlobAssetReference<SpellBlobs> SpellsDatabaseRef;

        public void Execute([ChunkIndexInQuery] int chunkIndex, Entity entity, ref DynamicBuffer<SpellActivationRequest> activationRequestBuffer, DynamicBuffer<ActiveSpell> activeSpellsBuffer)
        {
            if (activationRequestBuffer.IsEmpty)
                return;

            ref var spellsDatabase = ref SpellsDatabaseRef.Value.Spells;

            foreach (var activationRequest in activationRequestBuffer)
            {
                if (SpellIndexMap.TryGetValue(new SpellKey { Value = activationRequest.ID }, out var spellIndex))
                {
                    ref var spellData = ref SpellsDatabaseRef.Value.Spells[spellIndex];

                    // If spell is an active spell with cooldown
                    if (spellData.BaseCooldown > 0)
                    {
                        // Add spell to ActiveSpells buffer
                        activeSpellsBuffer.Add(new ActiveSpell
                        {
                            DatabaseIndex = spellIndex,
                            Level = 1,
                            CooldownTimer = spellData.BaseCooldown
                        });

                    }
                    // Else if spell is a passive spell that should be instanciated once
                    else
                    {
                        // Create CastSpellRequest to launch the spell 
                        var castRequestEntity = ECB.CreateEntity(chunkIndex);
                        ECB.AddComponent(chunkIndex, castRequestEntity, new CastSpellRequest
                        {
                            Caster = entity,
                            Target = Entity.Null,
                            DatabaseIndex = spellIndex,
                        });
                    }
                }
            }

            // Clear request buffer
            activationRequestBuffer.Clear();
        }
    }
}