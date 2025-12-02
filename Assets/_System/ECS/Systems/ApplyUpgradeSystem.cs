using Unity.Entities;
using Unity.Burst;
using Unity.Collections;

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

        var upgradesDatabaseEntity = SystemAPI.GetSingletonEntity<UpgradesDatabase>();
        var upgradesDatabase = SystemAPI.GetComponent<UpgradesDatabase>(upgradesDatabaseEntity);

        var gameStateEntity = SystemAPI.GetSingletonEntity<GameState>();

        var applyUpgradeJob = new ApplyUpgradeJob()
        {
            ECB = ecb.AsParallelWriter(),
            PlayerEntity = playerEntity,
            GameStateEntity = gameStateEntity,
            UpgradesDatabaseRef = upgradesDatabase.Blobs
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

        public void Execute([ChunkIndexInQuery] int chunkIndex, Entity requestEntity, in ApplyUpgradeRequest request)
        {
            int upgradeIndex = request.DatabaseIndex;
            ref var upgradeData = ref UpgradesDatabaseRef.Value.Upgrades[upgradeIndex];

            switch (upgradeData.UpgradeType)
            {
                case EUpgradeType.Stat:
                    var statModifier = new StatModifier()
                    {
                        Type = upgradeData.StatType,
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

                case EUpgradeType.Spell:
                    //@todo
                    break;

                default:
                    break;
            }
            // Clear upgrades selection buffer 
            ECB.SetBuffer<UpgradeSelectionElement>(chunkIndex, GameStateEntity);

            // Destroy ApplyUpgradeRequest
            ECB.DestroyEntity(chunkIndex, requestEntity);
        }
    }
}
