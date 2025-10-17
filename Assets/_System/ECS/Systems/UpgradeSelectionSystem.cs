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
        state.RequireForUpdate<UpgradesDatabase>();
        state.RequireForUpdate<GameState>();
        state.RequireForUpdate<PlayerLevelUpFlag>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var playerEntity = SystemAPI.GetSingletonEntity<Player>();
        if (!SystemAPI.HasComponent<PlayerLevelUpFlag>(playerEntity))
            return;

        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        var upgradesDatabaseEntity = SystemAPI.GetSingletonEntity<UpgradesDatabase>();
        var upgradesDatabase = SystemAPI.GetComponent<UpgradesDatabase>(upgradesDatabaseEntity);


        var gameStateEntity = SystemAPI.GetSingletonEntity<GameState>();
        var gameState = SystemAPI.GetSingleton<GameState>();
        var upgradesSelectionBuffer = SystemAPI.GetSingletonBuffer<UpgradeSelectionElement>();

        var upgradeSelectionJob = new SelectUpgradeJob()
        {
            ECB = ecb,
            PlayerEntity = playerEntity,
            GameStateEntity = gameStateEntity,
            UpgradesDatabaseRef = upgradesDatabase.Blobs,
            Seed = (uint)(SystemAPI.Time.ElapsedTime * 1000) + 1
        };
        state.Dependency = upgradeSelectionJob.Schedule(state.Dependency);
    }

    [BurstCompile]
    private struct SelectUpgradeJob : IJob
    {
        public EntityCommandBuffer ECB;
        public Entity PlayerEntity;
        public Entity GameStateEntity;
        [ReadOnly] public BlobAssetReference<UpgradeBlobs> UpgradesDatabaseRef;
        public uint Seed;

        public void Execute()
        {
            var random = Random.CreateFromIndex(Seed);
            ref BlobArray<UpgradeBlob> upgradesDatabase = ref UpgradesDatabaseRef.Value.Upgrades;

            int upgradesDatabaseLength = upgradesDatabase.Length;
            int upgradesChoicesCount = 3;

            // Clear buffer
            ECB.SetBuffer<UpgradeSelectionElement>(GameStateEntity);
            for (int i = 0; i < upgradesChoicesCount; i++)
            {
                int index = random.NextInt(0, upgradesDatabaseLength);
                ref var upgrade = ref upgradesDatabase[index];

                ECB.AppendToBuffer<UpgradeSelectionElement>(GameStateEntity, new UpgradeSelectionElement()
                {
                    DatabaseIndex = index
                });
            }
            //@todo use SelectUpgrade Flag in GameManager instead of checking if the buffer is fullfiled

            ECB.RemoveComponent<PlayerLevelUpFlag>(PlayerEntity);
        }
    }
}
