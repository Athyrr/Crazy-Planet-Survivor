using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

/// <summary>
/// System that handles activation of spells when requested. Activate Initial spells for entities then when a new spell is unlocked..
/// </summary>
[UpdateInGroup(typeof(InitializationSystemGroup))]
[BurstCompile]
public partial struct SpellActivationSystem : ISystem
{
    private NativeHashMap<SpellKey, int> _spellIndexMap;
    // Last stored spells database blob ref
    private BlobAssetReference<SpellBlobs> _lastBlobRef;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<SpellsDatabase>();

        _spellIndexMap = new NativeHashMap<SpellKey, int>(64, Allocator.Persistent);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        if (_spellIndexMap.IsCreated)
            _spellIndexMap.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var database = SystemAPI.GetSingleton<SpellsDatabase>();

        JobHandle dependency = state.Dependency;

        if (database.Blobs != _lastBlobRef)
        {
            // Update cached db ref
            _lastBlobRef = database.Blobs;

            var buildMapJob = new BuildSpellMapJob
            {
                SpellMap = _spellIndexMap,
                SpellsDatabaseRef = database.Blobs
            };

            dependency = buildMapJob.Schedule(dependency);
        }

        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        var activateSpellJob = new ActivateSpellJob()
        {
            ECB = ecb.AsParallelWriter(),

            SpellIndexMap = _spellIndexMap,
            SpellsDatabaseRef = database.Blobs
        };
        state.Dependency = activateSpellJob.ScheduleParallel(dependency);
    }

    [BurstCompile]
    private struct BuildSpellMapJob : IJob
    {
        public NativeHashMap<SpellKey, int> SpellMap;
        [ReadOnly] public BlobAssetReference<SpellBlobs> SpellsDatabaseRef;

        public void Execute()
        {
            SpellMap.Clear();

            ref var spellsDb = ref SpellsDatabaseRef.Value.Spells;

            if (SpellMap.Capacity < spellsDb.Length)
                SpellMap.Capacity = spellsDb.Length;

            for (int i = 0; i < spellsDb.Length; i++)
                SpellMap.TryAdd(new SpellKey { Value = spellsDb[i].ID }, i);
        }
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
                    // if spell is already active, skip
                    bool isAlreadyActive = HasSpell(ref activeSpellsBuffer, spellIndex);

                    if (isAlreadyActive)
                        continue;

                    ref var spellData = ref SpellsDatabaseRef.Value.Spells[spellIndex];

                    activeSpellsBuffer.Add(new ActiveSpell
                    {
                        DatabaseIndex = spellIndex,
                        Level = 1,
                        DamageMultiplier = 1f,
                        CooldownMultiplier = 1f,
                        AreaMultiplier = 1f,
                        SpeedMultiplier = 1f,
                        DurationMultiplier = 1f,
                        RangeMultiplier = 1f,
                        TickRateMultiplier = 1f,
                        LifetimeMultiplier = 1f,

                        BonusAmount = 0,
                        BonusBounces = 0,
                        BonusPierces = 0,

                        BonusCritChance = 0f,
                        BonusCritMultiplier = 0f,

                        CurrentCooldown = 0f
                    });


                    bool isPassiveOrPermanent = spellData.BaseCooldown <= 0;
                    if (isPassiveOrPermanent)
                    {
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

        private static bool HasSpell(ref DynamicBuffer<ActiveSpell> activeSpellsBuffer, int spellIndex)
        {
            bool isAlreadyActive = false;
            for (int i = 0; i < activeSpellsBuffer.Length; i++)
            {
                if (activeSpellsBuffer[i].DatabaseIndex == spellIndex)
                {
                    isAlreadyActive = true;
                    break;
                }
            }

            return isAlreadyActive;
        }
    }
}