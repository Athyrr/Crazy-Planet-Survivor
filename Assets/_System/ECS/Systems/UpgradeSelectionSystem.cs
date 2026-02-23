using Unity.Collections;
using Unity.Mathematics;
using Unity.Entities;
using Unity.Burst;
using Unity.Jobs;

/// <summary>
/// ECS System that handles upgrade selection on each level up.
/// It gets the ref of the player upgrade database and calculates 3 upgrades to give to a buffer of SelectedUpgradeElement.
/// Choosing an upgrade is managed by UpgradeSelectionComponent (Monobehavior)
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[BurstCompile]
public partial struct UpgradeSelectionSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameState>();
        state.RequireForUpdate<SpellsDatabase>();
        state.RequireForUpdate<UpgradesDatabase>();
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

        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        var upgradesDatabase = SystemAPI.GetSingleton<UpgradesDatabase>();
        var spellsDatabase = SystemAPI.GetSingleton<SpellsDatabase>();
        var gameStateEntity = SystemAPI.GetSingletonEntity<GameState>();

        var selectUpgradeJob = new SelectUpgradeJob()
        {
            ECB = ecb,
            Seed = (uint)(SystemAPI.Time.ElapsedTime * 1000) + 1,

            PlayerEntity = playerEntity,
            GameStateEntity = gameStateEntity,

            UpgradesDatabaseRef = upgradesDatabase.Blobs,
            SpellsDatabaseRef = spellsDatabase.Blobs,

            PlayerExperienceLookup = SystemAPI.GetComponentLookup<PlayerExperience>(true),
            ActiveSpellLookup = SystemAPI.GetBufferLookup<ActiveSpell>(true),
            StatsUpgradePoolBufferLookup = SystemAPI.GetBufferLookup<StatsUpgradePoolBufferElement>(true),
            SpellsUpgradePoolBufferLookup = SystemAPI.GetBufferLookup<SpellsUpgradePoolBufferElement>(true)
        };

        state.Dependency = selectUpgradeJob.Schedule(state.Dependency);
    }

    //@todo Burst and remove log
    //[BurstCompile]
    private struct SelectUpgradeJob : IJob
    {
        public EntityCommandBuffer ECB;

        public uint Seed;
        public Entity PlayerEntity;
        public Entity GameStateEntity;

        [ReadOnly] public BlobAssetReference<UpgradeBlobs> UpgradesDatabaseRef;
        [ReadOnly] public BlobAssetReference<SpellBlobs> SpellsDatabaseRef;

        [ReadOnly] public ComponentLookup<PlayerExperience> PlayerExperienceLookup;
        [ReadOnly] public BufferLookup<ActiveSpell> ActiveSpellLookup;
        [ReadOnly] public BufferLookup<StatsUpgradePoolBufferElement> StatsUpgradePoolBufferLookup;
        [ReadOnly] public BufferLookup<SpellsUpgradePoolBufferElement> SpellsUpgradePoolBufferLookup;

        public void Execute()
        {
            var random = Random.CreateFromIndex(Seed);

            if (!PlayerExperienceLookup.TryGetComponent(PlayerEntity, out var experience))
                return;

            // Drop spell upgrades every 4 levels
            bool mustDropSpell = (experience.Level % 4) == 0;

            var candiates = new NativeList<int>(Allocator.Temp);

            if (mustDropSpell)
            {
                SetSpellCandiates(ref candiates);

                if (candiates.Length <= 0)
                    mustDropSpell = false;
            }

            if (!mustDropSpell)
                SetStatCanditates(ref candiates);

            // Clear previous selection
            ECB.SetBuffer<UpgradeSelectionBufferElement>(GameStateEntity);

            // Pick 3 upgrades from candidates
            int countToPick = math.min(3, candiates.Length);

            for (int i = 0; i < countToPick; i++)
            {
                // Pick random index from candidates
                int indexInList = random.NextInt(0, candiates.Length);
                int globalDbIndex = candiates[indexInList];

                ECB.AppendToBuffer<UpgradeSelectionBufferElement>(GameStateEntity, new UpgradeSelectionBufferElement()
                {
                    DatabaseIndex = globalDbIndex
                });

                // Avoid picking same upgrade again
                candiates.RemoveAtSwapBack(indexInList);
            }

            UnityEngine.Debug.LogWarning("Candidates length: " + candiates.Length);

            // Add display upgrades flag 
            ECB.AddComponent<OpenUpgradesSelectionMenuRequest>(GameStateEntity);
            // Remove player lvl up request
            ECB.RemoveComponent<PlayerLevelUpRequest>(PlayerEntity);

            candiates.Dispose();
        }

        private void SetStatCanditates(ref NativeList<int> candiates)
        {
            if (!StatsUpgradePoolBufferLookup.TryGetBuffer(PlayerEntity, out var statsUpgradePool))
                return;

            for (int i = 0; i < statsUpgradePool.Length; i++)
                candiates.Add(statsUpgradePool[i].DatabaseIndex);
        }

        private void SetSpellCandiates(ref NativeList<int> candiates)
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
                    // Valid only if player has the spell

                    // Target Spell ID
                    if (upgradeData.SpellID != ESpellID.None)
                    {
                        // if player has this spell unlocked
                        if (HasSpell(upgradeData.SpellID, activeSpells, ref spellBlobs))
                            isValid = true;
                    }
                    // Target tag 
                    else if (upgradeData.SpellTags != ESpellTag.None)
                    {
                        if (HasSpellWithTag(upgradeData.SpellTags, activeSpells, ref spellBlobs))
                            isValid = true;
                    }
                }

                if (isValid)
                    candiates.Add(globalIndex);
            }
        }

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

        private bool HasSpellWithTag(ESpellTag tag, DynamicBuffer<ActiveSpell> activeSpells, ref BlobArray<SpellBlob> spellBlobs)
        {
            for (int i = 0; i < activeSpells.Length; i++)
            {
                SpellBlob spellBlob = spellBlobs[activeSpells[i].DatabaseIndex];
                if ((spellBlob.Tag & tag) != 0)
                    return true;
            }

            return false;
        }
    }
}
