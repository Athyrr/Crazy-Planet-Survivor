using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[BurstCompile]
public partial struct ApplyUpgradeSystem : ISystem
{
    private EntityQuery _subSpellsSpawnerQuery;
    private EntityQuery _activeAurasQuery;

    private BufferLookup<ActiveSpell> _activeSpellsBufferLookup;
    private BufferLookup<SpellModifier> _spellModifiersLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Player>();
        state.RequireForUpdate<GameState>();
        state.RequireForUpdate<UpgradesDatabase>();
        // state.RequireForUpdate<ApplyUpgradeRequest>();

        _activeSpellsBufferLookup = SystemAPI.GetBufferLookup<ActiveSpell>(false);
        _spellModifiersLookup = SystemAPI.GetBufferLookup<SpellModifier>(false);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton<GameState>(out var gameState))
            return;

        // if (gameState.State != EGameState.Running && gameState.State != EGameState.UpgradeSelection)
        //     return;

        var gameStateEntity = SystemAPI.GetSingletonEntity<GameState>();

        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecbUpgrade = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
        var ecbAmulet = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        var upgradesDatabaseEntity = SystemAPI.GetSingletonEntity<UpgradesDatabase>();
        var upgradesDatabase = SystemAPI.GetComponent<UpgradesDatabase>(upgradesDatabaseEntity);
        var spellsDatabase = SystemAPI.GetSingleton<SpellsDatabase>();

        var amuletsDatabaseEntity = SystemAPI.GetSingletonEntity<AmuletsDatabase>();
        var amuletsDatabase = SystemAPI.GetComponent<AmuletsDatabase>(amuletsDatabaseEntity);

        _activeSpellsBufferLookup.Update(ref state);
        _spellModifiersLookup.Update(ref state);

        var applyUpgradeJob = new ApplyUpgradeJob()
        {
            ECB = ecbUpgrade.AsParallelWriter(),

            GameStateEntity = gameStateEntity,

            UpgradesDatabaseRef = upgradesDatabase.Blobs,
            SpellsDatabaseRef = spellsDatabase.Blobs,

            ActiveSpellLookup = _activeSpellsBufferLookup,
            SpellModifierLookup = _spellModifiersLookup
        };
        var applyUpgradeJobHandle = applyUpgradeJob.ScheduleParallel(state.Dependency);

        var applyAmuletJob = new ApplyAmuletJob()
        {
            ECB = ecbAmulet.AsParallelWriter(),

            AmuletsDatabaseRef = amuletsDatabase.Blobs,
            SpellsDatabaseRef = spellsDatabase.Blobs,

            ActiveSpellLookup = _activeSpellsBufferLookup,
            SpellModifierLookup = _spellModifiersLookup
        };
        var applyAmuletJobHandle = applyAmuletJob.ScheduleParallel(state.Dependency);

        state.Dependency = JobHandle.CombineDependencies(applyUpgradeJobHandle, applyAmuletJobHandle);
    }

    [BurstCompile]
    private partial struct ApplyUpgradeJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter ECB;
        public Entity GameStateEntity;

        [ReadOnly] public BlobAssetReference<UpgradeBlobs> UpgradesDatabaseRef;
        [ReadOnly] public BlobAssetReference<SpellBlobs> SpellsDatabaseRef;

        [NativeDisableParallelForRestriction] public BufferLookup<ActiveSpell> ActiveSpellLookup;
        [NativeDisableParallelForRestriction] public BufferLookup<SpellModifier> SpellModifierLookup;

        public void Execute(
            [ChunkIndexInQuery] int chunkIndex,
            Entity playerEntity,
            in ApplyUpgradeRequest request,
            ref CoreStats playerCoreStats,
            ref Health health)
        {
            ref var upgrade = ref UpgradesDatabaseRef.Value.Upgrades[request.DatabaseIndex];
            bool needSpellUpdate = false;

            // todo check for keeping StatModifer Buffer
            // PLAYER GLOBAL STAT UPGRADE: STAT (Modif Directly)
            if (upgrade.UpgradeType == EUpgradeType.PlayerStat && upgrade.CharacterStat != ECharacterStat.None )
            {
                ApplyPlayerStatUpgrade(ref playerCoreStats, ref health, upgrade.CharacterStat, upgrade.Value,
                    ref needSpellUpdate);
            }

            // SPECIFIC SPELL UPGRADE: STAT OR NEW TAG (Modif ActiveSpell Buffer)
            else if (upgrade.UpgradeType == EUpgradeType.UpgradeSpell)
            {
                // todo handle case where the upgrade adds a tag to the spell (ex: make fireball also apply burn or explode on impact).
                if (ActiveSpellLookup.TryGetBuffer(playerEntity, out var spells))
                {
                    var newTags = upgrade.SpellTags != ESpellTag.None ? upgrade.SpellTags : ESpellTag.None;

                    // Find spell from database
                    ref var allSpells = ref SpellsDatabaseRef.Value.Spells;
                    for (int i = 0; i < spells.Length; i++)
                    {
                        var spell = spells[i];
                        if (allSpells[spell.DatabaseIndex].ID == upgrade.SpellID)
                        {
                            // Modify spell 
                            ApplySpellUpgrade(ref spell, upgrade.SpellStat, upgrade.Value, newTags);

                            // Level up
                            spell.Level++;

                            // Save to buffer
                            spells[i] = spell;

                            // Set spell as dirty
                            needSpellUpdate = true;
                            break;
                        }
                    }
                }
            }

            // UNLOCK NEW SPELL (Send SpellActivationRequest)
            else if (upgrade.UpgradeType == EUpgradeType.UnlockSpell)
            {
                ECB.AppendToBuffer<SpellActivationRequest>(chunkIndex, playerEntity, new SpellActivationRequest()
                {
                    ID = upgrade.SpellID
                });
            }

            // TAGGED SPELLS UPGRADE: TARGETING ALL SPELLS WITH THE SAME TAG (Modif spell Modifier Buffer)
            else if (upgrade.UpgradeType == EUpgradeType.UpgradeSpell && upgrade.SpellTags != ESpellTag.None &&
                     upgrade.SpellID == ESpellID.None)
            {
                if (SpellModifierLookup.TryGetBuffer(playerEntity, out var spellModifiers))
                {
                    spellModifiers.Add(new SpellModifier
                    {
                        RequiredTags = upgrade.SpellTags,
                        SpellStat = upgrade.SpellStat, // ex: Damage
                        Value = upgrade.Value, // ex: 0.1
                        Strategy = EModiferStrategy.Multiply // ou Add selon logique
                    });
                    needSpellUpdate = true;
                }
            }

            // if modifing spells -> send request for SpellStatsUpdateSystem
            if (needSpellUpdate)
            {
                ECB.AddComponent<SpellStatsCalculationRequest>(chunkIndex, playerEntity);
            }

            // Remove request from player
            ECB.RemoveComponent<ApplyUpgradeRequest>(chunkIndex, playerEntity);

            // Clear upgrades selection buffer 
            ECB.SetBuffer<UpgradeSelectionBufferElement>(chunkIndex, GameStateEntity);
        }
    }

    [BurstCompile]
    private partial struct ApplyAmuletJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter ECB;

        [ReadOnly] public BlobAssetReference<AmuletBlobs> AmuletsDatabaseRef;
        [ReadOnly] public BlobAssetReference<SpellBlobs> SpellsDatabaseRef;

        [NativeDisableParallelForRestriction] public BufferLookup<ActiveSpell> ActiveSpellLookup;
        [NativeDisableParallelForRestriction] public BufferLookup<SpellModifier> SpellModifierLookup;

        public void Execute([ChunkIndexInQuery] int chunkIndex, Entity playerEntity, in ApplyAmuletRequest request,
            ref CoreStats playerCoreStats, ref Health health)
        {
            // Remove request from player
            ECB.RemoveComponent<ApplyAmuletRequest>(chunkIndex, playerEntity);
            
            ref var amulet = ref AmuletsDatabaseRef.Value.Amulets[request.DatabaseIndex];
            bool needSpellUpdate = false;

            for (int i = 0; i < amulet.Modifiers.Length; i++)
            {
                var mod = amulet.Modifiers[i];

                // Player Stat Upgrade
                if (mod.UpgradeType == EUpgradeType.PlayerStat)
                {
                    ApplyPlayerStatUpgrade(ref playerCoreStats, ref health, mod.CharacterStat, mod.Value,
                        ref needSpellUpdate);
                }

                // Specific Spell Upgrade
                else if (mod.UpgradeType == EUpgradeType.UpgradeSpell && mod.SpellID != ESpellID.None)
                {
                    if (ActiveSpellLookup.TryGetBuffer(playerEntity, out var spells))
                    {
                        var newTags = mod.SpellTags != ESpellTag.None ? mod.SpellTags : ESpellTag.None;
                        ref var allSpells = ref SpellsDatabaseRef.Value.Spells;

                        for (int j = 0; j < spells.Length; j++)
                        {
                            var spell = spells[j];
                            if (allSpells[spell.DatabaseIndex].ID == mod.SpellID)
                            {
                                ApplySpellUpgrade(ref spell, mod.SpellStat, mod.Value, newTags);
                                spells[j] = spell;
                                needSpellUpdate = true;
                                break;
                            }
                        }
                    }
                }

                // Tagged spells
                else if (mod.UpgradeType == EUpgradeType.UpgradeSpell && mod.SpellTags != ESpellTag.None &&
                         mod.SpellID == ESpellID.None)
                {
                    if (SpellModifierLookup.TryGetBuffer(playerEntity, out var spellModifiers))
                    {
                        spellModifiers.Add(new SpellModifier
                        {
                            RequiredTags = mod.SpellTags,
                            SpellStat = mod.SpellStat,
                            Value = mod.Value,
                            Strategy = mod.Strategy
                        });
                        needSpellUpdate = true;
                    }
                }
            }

            if (needSpellUpdate)
            {
                ECB.AddComponent<SpellStatsCalculationRequest>(chunkIndex, playerEntity);
            }


        }
    }


    private static void ApplyPlayerStatUpgrade(ref CoreStats playerCoreStats, ref Health health, ECharacterStat stat,
        float value, ref bool needSpellUpdate)
    {
        switch (stat)
        {
            case ECharacterStat.MaxHealth:
                playerCoreStats.MaxHealthMultiplier += value;
                health.Value += (int)(playerCoreStats.BaseMaxHealth * value); // todo Heal ?
                break;
            case ECharacterStat.Health:
                health.Value = math.min(health.Value + (int)value,
                    (int)(playerCoreStats.BaseMaxHealth * playerCoreStats.MaxHealthMultiplier));
                break;
            case ECharacterStat.Armor:
                playerCoreStats.BaseArmor += value;
                break;
            case ECharacterStat.Speed:
                playerCoreStats.MoveSpeedMultiplier += value;
                break;
            case ECharacterStat.CollectRange:
                playerCoreStats.PickupRangeMultiplier += value;
                break;
            case ECharacterStat.Damage:
                playerCoreStats.GlobalDamageMultiplier += value;
                needSpellUpdate = true;
                break;
            case ECharacterStat.CooldownReduction:
                playerCoreStats.GlobalCooldownMultiplier -= value;
                needSpellUpdate = true;
                break;
            case ECharacterStat.AreaSize:
                playerCoreStats.GlobalSpellAreaMultiplier += value;
                needSpellUpdate = true;
                break;
            case ECharacterStat.SizeMultiplier:
                playerCoreStats.GlobalSpellSizeMultiplier += value;
                needSpellUpdate = true;
                break;
            case ECharacterStat.BounceCount:
                playerCoreStats.GlobalBounceBonus += (int)value;
                needSpellUpdate = true;
                break;
            case ECharacterStat.PierceCount:
                playerCoreStats.GlobalPierceBonus += (int)value;
                needSpellUpdate = true;
                break;
            case ECharacterStat.CritChance:
                playerCoreStats.CritChance += value;
                needSpellUpdate = true;
                break;
            case ECharacterStat.CritDamage:
                playerCoreStats.CritDamageMultiplier += value;
                needSpellUpdate = true;
                break;
            // todo other character stats (ex SpellSize, SpellSpeed, Duration...)
            // todo Handle status (burn duration, slow strength...)  + use to calculate current stats in active effects system
        }
    }

    private static void ApplySpellUpgrade(ref ActiveSpell spell, ESpellStat stat, float value, ESpellTag newTags)
    {
        // upgrade.Value is a DELTA (e.g., 0.1 for +10%)
        // ALL percent-based modifiers should be ADDITIVE (+=).
        switch (stat)
        {
            case ESpellStat.Damage:
                spell.LocalDamageBonusMultiplier += value;
                break;

            case ESpellStat.Cooldown:
                spell.LocalCooldownBonusPercent += value;
                break;

            case ESpellStat.Speed:
                spell.LocalSpeedBonusPercent += value;
                break;

            case ESpellStat.AreaOfEffect:
                spell.LocalAreaBonusMultiplier += value;
                break;

            case ESpellStat.Range:
                spell.LocalRangeBonusMultiplier += value;
                break;

            case ESpellStat.Duration:
                spell.LocalDurationBonusPercent += value;
                break;

            case ESpellStat.Amount:
                spell.LocalAmountBonus += (int)value;
                break;

            case ESpellStat.TickRate:
                spell.LocalTickRateBonusMultiplier += value;
                break;

            case ESpellStat.BounceCount:
                spell.LocalBounceBonus += (int)value;
                break;

            case ESpellStat.PierceCount:
                spell.LocalPierceBonus += (int)value;
                break;

            case ESpellStat.CritChance:
                spell.LocalCritChanceBonusPercent += value;
                break;

            case ESpellStat.CritDamage:
                spell.LocalCritDamageBonus += value;
                break;

            case ESpellStat.Size:
                spell.LocalSizeBonusMultiplier += value;
                break;
        }

        // Add new tags
        if (newTags != ESpellTag.None)
        {
            spell.AddedTags |= newTags;
        }
    }
}