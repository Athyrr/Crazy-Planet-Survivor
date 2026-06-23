using Unity.Collections;
using Unity.Mathematics;
using Unity.Entities;
using Unity.Burst;
using Unity.Jobs;

/// <summary>
/// ECS System that handles upgrade selection on each level up.
/// It reads the player's upgrade pools and writes up to 3 candidate upgrades to the
/// <see cref="UpgradeSelectionBufferElement"/> buffer on the GameState entity.
/// Stat upgrades are drawn with a per-card rarity roll weighted by base weights and the
/// player's Luck (see <c>CpRaritySettings</c> / <see cref="RaritySettings"/>); spell upgrades
/// are drawn uniformly. The "spell levels" cadence is configurable in the rarity settings.
/// Choosing an upgrade is handled by the UI (UpgradeSelectionView).
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[BurstCompile]
public partial struct UpgradeSelectionSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        state.RequireForUpdate<Player>();
        state.RequireForUpdate<GameState>();
        state.RequireForUpdate<SpellsDatabase>();
        state.RequireForUpdate<UpgradesDatabase>();
        state.RequireForUpdate<RaritySettings>();
        state.RequireForUpdate<PlayerLevelUpRequest>();

        state.RequireForUpdate<StatsUpgradePoolBufferElement>();
        state.RequireForUpdate<SpellsUpgradePoolBufferElement>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var playerEntity = SystemAPI.GetSingletonEntity<Player>();
        if (!SystemAPI.HasComponent<PlayerLevelUpRequest>(playerEntity))
            return;

        var pendingLvls = SystemAPI.GetComponent<PlayerLevelUpRequest>(playerEntity).PendingLevels;
        if (pendingLvls <= 0)
            return;

        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        var upgradesDatabase = SystemAPI.GetSingleton<UpgradesDatabase>();
        var spellsDatabase = SystemAPI.GetSingleton<SpellsDatabase>();
        var raritySettings = SystemAPI.GetSingleton<RaritySettings>();
        var gameStateEntity = SystemAPI.GetSingletonEntity<GameState>();

        var selectUpgradeJob = new SelectUpgradeJob()
        {
            ECB = ecb,
            Seed = (uint)(SystemAPI.Time.ElapsedTime * 1000) + 1,

            PlayerEntity = playerEntity,
            GameStateEntity = gameStateEntity,

            UpgradesDatabaseRef = upgradesDatabase.Blobs,
            SpellsDatabaseRef = spellsDatabase.Blobs,
            RaritySettingsRef = raritySettings.Blob,

            PlayerExperienceLookup = SystemAPI.GetComponentLookup<PlayerExperience>(true),
            PlayerLevelRequestLookup = SystemAPI.GetComponentLookup<PlayerLevelUpRequest>(false),
            CoreStatsLookup = SystemAPI.GetComponentLookup<CoreStats>(true),
            ActiveSpellLookup = SystemAPI.GetBufferLookup<ActiveSpell>(true),
            StatsUpgradePoolBufferLookup = SystemAPI.GetBufferLookup<StatsUpgradePoolBufferElement>(true),
            SpellsUpgradePoolBufferLookup = SystemAPI.GetBufferLookup<SpellsUpgradePoolBufferElement>(true)
        };

        state.Dependency = selectUpgradeJob.Schedule(state.Dependency);
    }

    [BurstCompile]
    private struct SelectUpgradeJob : IJob
    {
        public EntityCommandBuffer ECB;

        public uint Seed;
        public Entity PlayerEntity;
        public Entity GameStateEntity;

        [ReadOnly] public BlobAssetReference<UpgradeBlobs> UpgradesDatabaseRef;
        [ReadOnly] public BlobAssetReference<SpellBlobs> SpellsDatabaseRef;
        [ReadOnly] public BlobAssetReference<RaritySettingsBlob> RaritySettingsRef;

        [ReadOnly] public ComponentLookup<PlayerExperience> PlayerExperienceLookup;
        public ComponentLookup<PlayerLevelUpRequest> PlayerLevelRequestLookup;
        [ReadOnly] public ComponentLookup<CoreStats> CoreStatsLookup;
        [ReadOnly] public BufferLookup<ActiveSpell> ActiveSpellLookup;
        [ReadOnly] public BufferLookup<StatsUpgradePoolBufferElement> StatsUpgradePoolBufferLookup;
        [ReadOnly] public BufferLookup<SpellsUpgradePoolBufferElement> SpellsUpgradePoolBufferLookup;

        // todo rework upgrade selection -> Allow new spell behavior even below the spell interval
        public void Execute()
        {
            var random = Random.CreateFromIndex(Seed);

            if (!PlayerExperienceLookup.TryGetComponent(PlayerEntity, out var experience))
                return;

            ref var rarity = ref RaritySettingsRef.Value;

            // Drop spell upgrades every Nth level (configurable in the rarity settings).
            int interval = math.max(1, rarity.SpellDropLevelInterval);
            bool mustDropSpell = (experience.Level % interval) == 0;

            // Clear previous selection
            ECB.SetBuffer<UpgradeSelectionBufferElement>(GameStateEntity);

            if (mustDropSpell)
            {
                var spellCandidates = new NativeList<int>(Allocator.Temp);
                SetSpellCandidates(ref spellCandidates);

                if (spellCandidates.Length > 0)
                    PickUniform(ref random, ref spellCandidates);
                else
                    PickStats(ref random); // no spell available -> fall back to stats

                spellCandidates.Dispose();
            }
            else
            {
                PickStats(ref random);
            }

            // Add display upgrades flag
            ECB.AddComponent<OpenUpgradesSelectionViewRequest>(GameStateEntity);
            // Remove player lvl up request

            var pendingLevels = PlayerLevelRequestLookup[PlayerEntity].PendingLevels;
            pendingLevels -= 1;
            ECB.SetComponent<PlayerLevelUpRequest>(PlayerEntity, new PlayerLevelUpRequest()
            {
                PendingLevels = pendingLevels
            });
        }

        /// <summary>Picks up to 3 stat upgrades, each card rolling its own (Luck-weighted) rarity.</summary>
        private void PickStats(ref Random random)
        {
            if (!StatsUpgradePoolBufferLookup.TryGetBuffer(PlayerEntity, out var statsUpgradePool))
                return;

            ref var upgrades = ref UpgradesDatabaseRef.Value.Upgrades;
            ref var rarity = ref RaritySettingsRef.Value;
            ref var spellBlobs = ref SpellsDatabaseRef.Value.Spells;

            // Player's equipped spells, used to gate behaviour-specific stat upgrades (Bounce, Pierce...).
            bool hasSpells = ActiveSpellLookup.TryGetBuffer(PlayerEntity, out var activeSpells);

            // Parallel lists: global db index + its rarity tier.
            var indices = new NativeList<int>(Allocator.Temp);
            var tiers = new NativeList<int>(Allocator.Temp);
            for (int i = 0; i < statsUpgradePool.Length; i++)
            {
                int idx = statsUpgradePool[i].DatabaseIndex;

                // Skip upgrades that require an equipped spell with a given tag (e.g. Bounce needs a Bouncing spell).
                ESpellTag requiredTag = upgrades[idx].RequiredSpellTag;
                if (requiredTag != ESpellTag.None &&
                    (!hasSpells || !HasAnySpellWithTag(requiredTag, activeSpells, ref spellBlobs)))
                    continue;

                indices.Add(idx);
                tiers.Add((int)upgrades[idx].Rarity);
            }

            // Luck → rare-weight multiplier for this draw.
            float luck = CoreStatsLookup.TryGetComponent(PlayerEntity, out var stats) ? stats.Luck : 0f;
            float luck01 = rarity.MaxLuck > 0f ? math.saturate(luck / rarity.MaxLuck) : 0f;
            float luckFactor = SampleLuckFactor(ref rarity.LuckSamples, luck01);

            // Per-tier weight = baseWeight * pow(luckFactor, tierIndex).
            var tierWeights = new NativeArray<float>(RarityConstants.Count, Allocator.Temp);
            for (int t = 0; t < RarityConstants.Count; t++)
            {
                float baseW = t < rarity.BaseWeights.Length ? rarity.BaseWeights[t] : 0f;
                tierWeights[t] = baseW * math.pow(luckFactor, t);
            }

            int countToPick = math.min(3, indices.Length);
            for (int pick = 0; pick < countToPick; pick++)
            {
                int chosenInList = RollCandidate(ref random, ref indices, ref tiers, tierWeights);
                if (chosenInList < 0)
                    break;

                ECB.AppendToBuffer(GameStateEntity, new UpgradeSelectionBufferElement
                {
                    DatabaseIndex = indices[chosenInList]
                });

                // Avoid offering the same upgrade twice.
                indices.RemoveAtSwapBack(chosenInList);
                tiers.RemoveAtSwapBack(chosenInList);
            }

            tierWeights.Dispose();
            indices.Dispose();
            tiers.Dispose();
        }

        /// <summary>
        /// Rolls a rarity tier among the tiers still present in the pool (weighted), then returns
        /// a random list position holding a candidate of that tier. Returns -1 if the pool is empty.
        /// </summary>
        private int RollCandidate(ref Random random, ref NativeList<int> indices, ref NativeList<int> tiers,
            NativeArray<float> tierWeights)
        {
            if (indices.Length == 0)
                return -1;

            // Sum the weights of tiers that still have at least one candidate.
            float weightSum = 0f;
            for (int t = 0; t < RarityConstants.Count; t++)
            {
                if (TierIsPresent(ref tiers, t))
                    weightSum += tierWeights[t];
            }

            int chosenTier = -1;
            if (weightSum > 0f)
            {
                float roll = random.NextFloat(0f, weightSum);
                float acc = 0f;
                for (int t = 0; t < RarityConstants.Count; t++)
                {
                    if (!TierIsPresent(ref tiers, t))
                        continue;
                    acc += tierWeights[t];
                    if (roll <= acc)
                    {
                        chosenTier = t;
                        break;
                    }
                }
            }

            // Fallback (all weights zero): pick any remaining candidate uniformly.
            if (chosenTier < 0)
                return random.NextInt(0, indices.Length);

            // Count candidates of the chosen tier, then pick the k-th one.
            int tierCount = 0;
            for (int i = 0; i < tiers.Length; i++)
                if (tiers[i] == chosenTier)
                    tierCount++;

            int target = random.NextInt(0, tierCount);
            int seen = 0;
            for (int i = 0; i < tiers.Length; i++)
            {
                if (tiers[i] != chosenTier)
                    continue;
                if (seen == target)
                    return i;
                seen++;
            }

            return -1;
        }

        private static bool TierIsPresent(ref NativeList<int> tiers, int tier)
        {
            for (int i = 0; i < tiers.Length; i++)
                if (tiers[i] == tier)
                    return true;
            return false;
        }

        private static float SampleLuckFactor(ref BlobArray<float> samples, float luck01)
        {
            int n = samples.Length;
            if (n == 0)
                return 1f;
            if (n == 1)
                return samples[0];

            float x = math.saturate(luck01) * (n - 1);
            int i0 = (int)math.floor(x);
            int i1 = math.min(i0 + 1, n - 1);
            float f = x - i0;
            return math.lerp(samples[i0], samples[i1], f);
        }

        /// <summary>Picks up to 3 candidates uniformly (used for spell draws — no rarity).</summary>
        private void PickUniform(ref Random random, ref NativeList<int> candidates)
        {
            int countToPick = math.min(3, candidates.Length);
            for (int i = 0; i < countToPick; i++)
            {
                int indexInList = random.NextInt(0, candidates.Length);
                ECB.AppendToBuffer(GameStateEntity, new UpgradeSelectionBufferElement
                {
                    DatabaseIndex = candidates[indexInList]
                });
                candidates.RemoveAtSwapBack(indexInList);
            }
        }

        private void SetSpellCandidates(ref NativeList<int> candidates)
        {
            if (!SpellsUpgradePoolBufferLookup.TryGetBuffer(PlayerEntity, out var spellsPool))
                return;

            if (!ActiveSpellLookup.TryGetBuffer(PlayerEntity, out var activeSpells))
                return;

            ref var upgradesBlobs = ref UpgradesDatabaseRef.Value.Upgrades;
            ref var spellBlobs = ref SpellsDatabaseRef.Value.Spells;

            for (int i = 0; i < spellsPool.Length; i++)
            {
                int globalIndex = spellsPool[i].DatabaseIndex;
                ref var upgradeData = ref upgradesBlobs[globalIndex];
                bool isValid = false;

                // if upgrade is new spell to unlock
                if (upgradeData.UpgradeType == EUpgradeType.UnlockSpell)
                {
                    // Valid only if player does not have the spell
                    if (!HasSpell(upgradeData.SpellID, activeSpells, ref spellBlobs))
                        isValid = true;
                }

                // if upgrade is spell upgrade
                else if (upgradeData.UpgradeType == EUpgradeType.UpgradeSpell)
                {
                    // Target Spell ID
                    if (upgradeData.SpellID != ESpellID.None)
                    {
                        // if player has not this spell unlocked
                        if (!HasSpell(upgradeData.SpellID, activeSpells, ref spellBlobs))
                            continue;

                        isValid = true;

                        if (upgradeData.SpellTags != ESpellTag.None)
                        {
                            if (SpellHasTag(upgradeData.SpellID, upgradeData.SpellTags, activeSpells, ref spellBlobs))
                            {
                                isValid = false;
                            }
                        }
                    }
                    // Target tag
                    else if (upgradeData.SpellTags != ESpellTag.None)
                    {
                        if (HasAnySpellWithTag(upgradeData.SpellTags, activeSpells, ref spellBlobs))
                            isValid = true;
                    }
                }

                if (isValid)
                    candidates.Add(globalIndex);
            }
        }

        /// <summary>
        /// Check if the player has the given spell
        /// </summary>
        private bool HasSpell(ESpellID id, DynamicBuffer<ActiveSpell> activeSpells, ref BlobArray<SpellBlob> spellBlobs)
        {
            if (activeSpells.IsEmpty)
                return false;

            for (int i = 0; i < activeSpells.Length; i++)
            {
                SpellBlob spellBlob = spellBlobs[activeSpells[i].DatabaseIndex];
                if (spellBlob.ID == id)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Check if a spell has a given tag.
        /// </summary>
        private bool SpellHasTag(ESpellID id, ESpellTag tag, DynamicBuffer<ActiveSpell> activeSpells,
            ref BlobArray<SpellBlob> spellBlobs)
        {
            for (int i = 0; i < activeSpells.Length; i++)
            {
                SpellBlob spellBlob = spellBlobs[activeSpells[i].DatabaseIndex];
                if (spellBlob.ID == id)
                {
                    ESpellTag combinedTags = spellBlob.Tag | activeSpells[i].AddedTags;
                    return (combinedTags & tag) != 0;
                }
            }

            return false;
        }

        /// <summary>
        /// Check if the player has at least one spell with a given tag
        /// </summary>
        private bool HasAnySpellWithTag(ESpellTag tag, DynamicBuffer<ActiveSpell> activeSpells,
            ref BlobArray<SpellBlob> spellBlobs)
        {
            for (int i = 0; i < activeSpells.Length; i++)
            {
                SpellBlob spellBlob = spellBlobs[activeSpells[i].DatabaseIndex];
                ESpellTag combinedTags = spellBlob.Tag | activeSpells[i].AddedTags;

                if ((combinedTags & tag) != 0)
                    return true;
            }

            return false;
        }
    }
}