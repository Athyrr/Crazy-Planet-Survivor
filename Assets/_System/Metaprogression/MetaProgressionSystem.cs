using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

[BurstCompile]
[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial struct MetaProgressionSystem : ISystem
{
    // todo handle meta shop + upgrade tree for each character
    //todo  upgrade tree on data asset + handle progression (dots vs poo bench)


    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        //state.RequireForUpdate<MetaprogresionView>();
    }

    //  todo Metaprog
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        state.Enabled = false;
    }

    public void OnDestroy(ref SystemState state)
    {
    }

    // todo 
    private partial struct PurchaseUpgradeJob : IJobEntity
    {
        private void Execute([ChunkIndexInQuery] int chunkIndex, in Entity entity,
            ref PlayerMetaProgression metaProgression)
        {
        }
    }
}


public partial struct PlayerMetaProgression : IComponentData
{
    public NativeHashMap<int, int> upgradeCostMap;
}