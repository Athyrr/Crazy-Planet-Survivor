using Unity.Collections;
using Unity.Entities;
using Unity.Physics;
using Unity.Burst;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[BurstCompile]
public partial struct ApplyUpgradeSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Player>();
        state.RequireForUpdate<UpgradesDatabase>();
        state.RequireForUpdate<ApplyUpgradeRequest>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        var playerEntity = SystemAPI.GetSingletonEntity<Player>();

        var spellsDatabase = SystemAPI.GetSingleton<SpellsDatabase>();
        var spellPrefabs = SystemAPI.GetSingletonBuffer<SpellPrefab>(true);
        var spellIndexMap = SystemAPI.GetSingleton<SpellToIndexMap>().Map;


        var upgradesDatabaseEntity = SystemAPI.GetSingletonEntity<UpgradesDatabase>();
        var upgradesDatabase = SystemAPI.GetComponent<UpgradesDatabase>(upgradesDatabaseEntity);

        var gameStateEntity = SystemAPI.GetSingletonEntity<GameState>();

        var applyUpgradeJob = new ApplyUpgradeJob()
        {
            ECB = ecb.AsParallelWriter(),

            PlayerEntity = playerEntity,

            GameStateEntity = gameStateEntity,

            UpgradesDatabaseRef = upgradesDatabase.Blobs,

            SpellsDatabaseRef = spellsDatabase.Blobs,
            SpellIndexMap = spellIndexMap,
            SpellPrefabs = spellPrefabs,

            ColliderLookup = SystemAPI.GetComponentLookup<PhysicsCollider>(true),
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
        [ReadOnly] public NativeHashMap<SpellKey, int> SpellIndexMap;
        [ReadOnly] public DynamicBuffer<SpellPrefab> SpellPrefabs;

        [ReadOnly] public ComponentLookup<PhysicsCollider> ColliderLookup;
        
        public void Execute([ChunkIndexInQuery] int chunkIndex, Entity requestEntity, in ApplyUpgradeRequest request)
        {
            int upgradeIndex = request.DatabaseIndex;
            ref var upgradeData = ref UpgradesDatabaseRef.Value.Upgrades[upgradeIndex];

            switch (upgradeData.UpgradeType)
            {
                case EUpgradeType.Stat:
                    var statModifier = new StatModifier()
                    {
                        StatID = upgradeData.Stat,
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
                    //@todo
                    break;

                default:
                    break;
            }

            // Clear upgrades selection buffer 
            ECB.SetBuffer<UpgradeSelectionBufferElement>(chunkIndex, GameStateEntity);

            // Destroy ApplyUpgradeRequest
            ECB.DestroyEntity(chunkIndex, requestEntity);
        }
    }
}
