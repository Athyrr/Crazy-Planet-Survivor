using Unity.Collections;
using Unity.Entities;
using Unity.Burst;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[BurstCompile]
public partial struct ApplyUpgradeSystem : ISystem
{
    private BufferLookup<ActiveSpell> _activeSpellsBufferLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Player>();
        state.RequireForUpdate<GameState>();
        state.RequireForUpdate<UpgradesDatabase>();
        state.RequireForUpdate<ApplyUpgradeRequest>();

        _activeSpellsBufferLookup = SystemAPI.GetBufferLookup<ActiveSpell>(false);
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
        state.Dependency = applyUpgradeJob.ScheduleParallel(state.Dependency);
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
                    spell.DamageMultiplier *= upgrade.Value;
                    break;

                case ESpellStat.Cooldown:
                    spell.CooldownMultiplier *= upgrade.Value;
                    break;

                case ESpellStat.Speed:
                    spell.SpeedMultiplier *= upgrade.Value;
                    break;

                case ESpellStat.AreaOfEffectSize:
                    spell.AreaMultiplier *= upgrade.Value;
                    break;

                case ESpellStat.Range:
                    spell.RangeMultiplier *= upgrade.Value;
                    break;

                case ESpellStat.Duration:
                    spell.LifetimeMultiplier *= upgrade.Value;
                    break;

                case ESpellStat.Amount:
                    spell.BonusAmount += (int)upgrade.Value;

                    //@todo check for ChildEntitiesSpawner and set dirty and set DesiredChildrenCount +Value


                    break;

                case ESpellStat.TickRate:
                    spell.TickRateMultiplier *= upgrade.Value;
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
