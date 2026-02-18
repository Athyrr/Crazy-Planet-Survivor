using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[BurstCompile]
public partial struct ApplyUpgradeSystem : ISystem
{
    private BufferLookup<ActiveSpell> _activeSpellsBufferLookup;

    private EntityQuery _subSpellsSpawnerQuery;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Player>();
        state.RequireForUpdate<GameState>();
        state.RequireForUpdate<UpgradesDatabase>();
        state.RequireForUpdate<ApplyUpgradeRequest>();

        _activeSpellsBufferLookup = SystemAPI.GetBufferLookup<ActiveSpell>(false);

        //_subSpellsSpawnerQuery = state.GetEntityQuery(ComponentType.ReadWrite<SubSpellsSpawner>(), ComponentType.ReadOnly<SpellLink>());
     
        // var types = new NativeArray<ComponentType>(2, Allocator.Temp);
        //types[0] = ComponentType.ReadWrite<SubSpellsSpawner>();
        //types[1] = ComponentType.ReadOnly<SpellLink>();
        //_subSpellsSpawnerQuery = state.GetEntityQuery(types);
        //types.Dispose();

        _subSpellsSpawnerQuery = new EntityQueryBuilder(Allocator.Temp).WithAllRW<SubSpellsSpawner>().WithAll<SpellLink>().Build(ref state);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var gameStateEntity = SystemAPI.GetSingletonEntity<GameState>();

        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        var playerEntity = SystemAPI.GetSingletonEntity<Player>();

        var upgradesDatabaseEntity = SystemAPI.GetSingletonEntity<UpgradesDatabase>();
        var upgradesDatabase = SystemAPI.GetComponent<UpgradesDatabase>(upgradesDatabaseEntity);

        var spellsDatabase = SystemAPI.GetSingleton<SpellsDatabase>();

        _activeSpellsBufferLookup.Update(ref state);

        var applyUpgradeJob = new ApplyUpgradeJob()
        {
            ECB = ecb.AsParallelWriter(),
            GameStateEntity = gameStateEntity,

            PlayerEntity = playerEntity,
            UpgradesDatabaseRef = upgradesDatabase.Blobs,
            SpellsDatabaseRef = spellsDatabase.Blobs,

            ActiveSpellLookup = _activeSpellsBufferLookup,
        };
        JobHandle upgradeHandle = applyUpgradeJob.ScheduleParallel(state.Dependency);

        var syncJob = new UpdateSubSpellsJob
        {
            ActiveSpellLookup = _activeSpellsBufferLookup,
            SpellsDatabaseRef = spellsDatabase.Blobs
        };

        state.Dependency = syncJob.ScheduleParallel(_subSpellsSpawnerQuery, upgradeHandle);
    }

    /// <summary>
    /// Updates sub entities spells (ex Fire orbs) afiter an upgrade.
    /// </summary>
    [BurstCompile]
    private partial struct UpdateSubSpellsJob : IJobEntity
    {
        [ReadOnly] public BufferLookup<ActiveSpell> ActiveSpellLookup;
        [ReadOnly] public BlobAssetReference<SpellBlobs> SpellsDatabaseRef;

        public void Execute(ref SubSpellsSpawner spawner, in SpellLink link)
        {
            if (!ActiveSpellLookup.TryGetBuffer(link.CasterEntity, out var activeSpells))
                return;

            ActiveSpell currentSpellData = default;
            bool found = false;

            for (int i = 0; i < activeSpells.Length; i++)
            {
                if (activeSpells[i].DatabaseIndex == link.DatabaseIndex)
                {
                    currentSpellData = activeSpells[i];
                    found = true;
                    break;
                }
            }

            if (!found)
                return;

            ref var baseSpellData = ref SpellsDatabaseRef.Value.Spells[link.DatabaseIndex];
            int totalAmount = baseSpellData.SubSpellsCount + currentSpellData.BonusAmount;

            //  Define the desired number of children to allow the relevant system to update the spell
            if (spawner.DesiredSubSpellsCount != totalAmount)
            {
                spawner.DesiredSubSpellsCount = totalAmount;
                spawner.IsDirty = true;
            }
        }
    }

    [BurstCompile]
    private partial struct ApplyUpgradeJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter ECB;
        public Entity GameStateEntity;

        [ReadOnly] public Entity PlayerEntity;

        [ReadOnly] public BlobAssetReference<UpgradeBlobs> UpgradesDatabaseRef;
        [ReadOnly] public BlobAssetReference<SpellBlobs> SpellsDatabaseRef;

        [NativeDisableParallelForRestriction]
        public BufferLookup<ActiveSpell> ActiveSpellLookup;

        public void Execute([ChunkIndexInQuery] int chunkIndex, Entity requestEntity, in ApplyUpgradeRequest request)
        {
            int upgradeIndex = request.DatabaseIndex;
            ref var upgradeData = ref UpgradesDatabaseRef.Value.Upgrades[upgradeIndex];

            switch (upgradeData.UpgradeType)
            {
                case EUpgradeType.Stat:
                    var statModifier = new StatModifier()
                    {
                        StatID = upgradeData.CharacterStat,
                        Strategy = upgradeData.ModifierStrategy,
                        Value = upgradeData.Value,
                    };
                    ECB.AppendToBuffer<StatModifier>(chunkIndex, PlayerEntity, statModifier);
                    ECB.AddComponent(chunkIndex, PlayerEntity, new RecalculateStatsRequest());
                    break;

                case EUpgradeType.UnlockSpell:
                    ECB.AppendToBuffer<SpellActivationRequest>(chunkIndex, PlayerEntity, new SpellActivationRequest()
                    {
                        ID = upgradeData.SpellID
                    });
                    break;

                case EUpgradeType.UpgradeSpell:
                    if (ActiveSpellLookup.TryGetBuffer(PlayerEntity, out var activeSpellsBuffer))
                    {
                        ref var spellBlobs = ref SpellsDatabaseRef.Value.Spells;

                        for (int i = 0; i < activeSpellsBuffer.Length; i++)
                        {
                            var activeSpell = activeSpellsBuffer[i];
                            ref var baseData = ref spellBlobs[activeSpell.DatabaseIndex];

                            // Target specific spell
                            bool matchID = upgradeData.SpellID != ESpellID.None && baseData.ID == upgradeData.SpellID;

                            // Target specific tag
                            bool matchTag = upgradeData.SpellID == ESpellID.None && (baseData.Tag & upgradeData.SpellTags) > 0;

                            if (matchID || matchTag)
                            {
                                ApplyModification(ref activeSpell, ref upgradeData);
                                activeSpellsBuffer[i] = activeSpell;
                            }
                        }
                    }
                    break;

                default:
                    break;
            }

            // Clear upgrades selection buffer 
            ECB.SetBuffer<UpgradeSelectionBufferElement>(chunkIndex, GameStateEntity);

            // Destroy ApplyUpgradeRequest
            ECB.DestroyEntity(chunkIndex, requestEntity);
        }

        private void ApplyModification(ref ActiveSpell spell, ref UpgradeBlob upgrade)
        {
            switch (upgrade.SpellStat)
            {
                case ESpellStat.Damage:
                    spell.DamageMultiplier += (upgrade.Value - 1.0f);
                    break;

                case ESpellStat.Cooldown:
                    // Cooldown is usually negative for reduction, e.g. 0.9 (-10%)
                    // If we want reduction, we add the delta
                    spell.CooldownMultiplier += (upgrade.Value - 1.0f);
                    break;

                case ESpellStat.Speed:
                    spell.SpeedMultiplier += (upgrade.Value - 1.0f);
                    break;

                case ESpellStat.AreaOfEffectSize:
                    spell.AreaMultiplier += (upgrade.Value - 1.0f);
                    break;

                case ESpellStat.Range:
                    spell.RangeMultiplier += (upgrade.Value - 1.0f);
                    break;

                case ESpellStat.Duration:
                    spell.LifetimeMultiplier += (upgrade.Value - 1.0f);
                    break;

                case ESpellStat.Amount:
                    spell.BonusAmount += (int)upgrade.Value;
                    break;

                case ESpellStat.TickRate:
                    spell.TickRateMultiplier += (upgrade.Value - 1.0f);
                    break;

                case ESpellStat.BounceCount:
                    spell.BonusBounces += (int)upgrade.Value;
                    break;

                case ESpellStat.PierceCount:
                    spell.BonusPierces += (int)upgrade.Value;
                    break;

                default:
                    break;
            }

            spell.Level++;
        }
    }
}
