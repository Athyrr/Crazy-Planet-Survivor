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

        var spellIndexMap = SystemAPI.GetSingleton<SpellToIndexMap>().Map;
        var database = SystemAPI.GetSingleton<SpellsDatabase>();

        var activateSpellJob = new ActivateSpellJob()
        {
            SpellIndexMap = spellIndexMap,
            SpellsDatabaseRef = database.Blobs
        };
        state.Dependency = activateSpellJob.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    [WithAll(typeof(SpellActivationRequest))]
    private partial struct ActivateSpellJob : IJobEntity
    {
        [ReadOnly] public NativeHashMap<SpellKey, int> SpellIndexMap;
        [ReadOnly] public BlobAssetReference<SpellBlobs> SpellsDatabaseRef;

        public void Execute(Entity entity, ref DynamicBuffer<SpellActivationRequest> activationRequestBuffer, DynamicBuffer<ActiveSpell> activeSpellsBuffer)
        {
            if (activationRequestBuffer.IsEmpty)
                return;

            ref var spellsDatabase = ref SpellsDatabaseRef.Value.Spells;

            foreach (var spellActivationRequest in activationRequestBuffer)
            {
                if (SpellIndexMap.TryGetValue(new SpellKey { Value = spellActivationRequest.ID }, out var spellIndex))
                {
                    activeSpellsBuffer.Add(new ActiveSpell
                    {
                        DatabaseIndex = spellIndex,
                        Level = 1,
                        CooldownTimer = spellsDatabase[spellIndex].BaseCooldown
                    });
                }
            }

            // Clear request buffer
            activationRequestBuffer.Clear();
        }
    }
}