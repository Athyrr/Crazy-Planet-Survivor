using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

/// <summary>
/// System that handles activation of spells when requested.
/// </summary>
[UpdateInGroup(typeof(InitializationSystemGroup))]
[BurstCompile]
public partial struct SpellActivationSystem : ISystem
{
    private NativeHashMap<SpellKey, int> _spellIndexMap;
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

        private void Execute(
            [ChunkIndexInQuery] int chunkIndex,
            Entity entity,
            ref DynamicBuffer<SpellActivationRequest> activationRequestBuffer,
            ref DynamicBuffer<ActiveSpell> activeSpellsBuffer)
        {
            if (activationRequestBuffer.IsEmpty)
                return;

            ref var spellsDatabase = ref SpellsDatabaseRef.Value.Spells;
            bool addedNewSpell = false;

            foreach (var activationRequest in activationRequestBuffer)
            {
                if (SpellIndexMap.TryGetValue(new SpellKey { Value = activationRequest.ID }, out var spellIndex))
                {
                    // if spell is already active, skip
                    if (HasSpell(ref activeSpellsBuffer, spellIndex))
                        continue;

                    ref var spellData = ref spellsDatabase[spellIndex];

                    activeSpellsBuffer.Add(new ActiveSpell
                    {
                        DatabaseIndex = spellIndex,
                        Level = 1,
                        CurrentCooldown = 0f,

                        // INPUTS
                        LocalDamageBonusMultiplier = 0f,
                        LocalAreaBonusMultiplier = 0f,
                        LocalSizeBonusMultiplier = 0f,
                        LocalSpeedBonusPercent = 0f,
                        LocalDurationBonusPercent = 0f,
                        LocalCooldownBonusPercent = 0f,
                        LocalRangeBonusMultiplier = 0f,
                        LocalTickRateBonusMultiplier = 0f,
                        LocalBounceRangeBonusMultiplier = 0f,

                        LocalAmountBonus = 0,
                        LocalBounceBonus = 0,
                        LocalPierceBonus = 0,

                        LocalCritChanceBonusPercent = 0f,
                        LocalCritDamageBonus = 0f,

                        AddedTags = ESpellTag.None,

                        // OUTPUTS
                        FinalDamage = 0f,
                        FinalArea = 0f,
                        FinalSize = 0f,
                        FinalSpeed = 0f,
                        FinalDuration = 0f,
                        FinalCooldown = 0f,
                        FinalRange = 0f,
                        FinalTickRate = 0f,
                        FinalAmount = 0,
                        FinalBounces = 0,
                        FinalPierces = 0,
                        FinalCritChance = 0f,
                        FinalCritDamageMultiplier = 0f,

                        TotalDamageDealt = 0f
                    });

                    addedNewSpell = true;

                    //  Cast immediately if passive or one shot spell (cooldown <= 0)
                    bool isPassiveOrPermanent = spellData.BaseCooldown <= 0;
                    if (isPassiveOrPermanent)
                    {
                        // todo add request on the caster entity
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
            
            // Recalculate stats if new spell added
            if (addedNewSpell)
                ECB.AddComponent<SpellStatsCalculationRequest>(chunkIndex, entity);

            // Clear request buffer
            activationRequestBuffer.Clear();
        }
        
        private static bool HasSpell(ref DynamicBuffer<ActiveSpell> activeSpellsBuffer, int spellIndex)
        {
            for (int i = 0; i < activeSpellsBuffer.Length; i++)
            {
                if (activeSpellsBuffer[i].DatabaseIndex == spellIndex)
                    return true;
            }
            return false;
        }
    }
}