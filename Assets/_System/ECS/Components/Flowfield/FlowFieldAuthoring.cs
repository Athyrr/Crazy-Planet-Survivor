using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using UnityEngine;

namespace _System.ECS.Components.Flowfield
{
    public class FlowFieldAuthoring: MonoBehaviour
    {
        #region Reference

        internal FlowFieldSO _flowFieldSo;

        #endregion
        
        #region ECS
        
        private class Baker : Baker<FlowFieldAuthoring>
        {
            public override void Bake(FlowFieldAuthoring authoring)
            {
                if (authoring._flowFieldSo)
                {
                    Debug.LogError("flowFieldSo missing in FlowFieldAuthoring!");
                    return;
                }

                var dbEntity = GetEntity(TransformUsageFlags.None);
                
                // Create spell blobs for data
                BlobBuilder builder = new BlobBuilder(Allocator.Temp); // but save in database after
                ref var root = ref builder.ConstructRoot<FlowFieldBlob>();
                
                var spellsDatabaseBlob = builder.CreateBlobAssetReference<FlowFieldBlob>(Allocator.Persistent);

                BlobBuilderArray<float3> position = builder.Construct(ref root.Positions, authoring._flowFieldSo.Data.Positions);
                BlobBuilderArray<int> neighbors = builder.Construct(ref root.Neighbors, authoring._flowFieldSo.Data.Neighbors);
                BlobBuilderArray<int> neighborsCounts = builder.Construct(ref root.NeighborCounts, authoring._flowFieldSo.Data.NeighborCounts);
                BlobBuilderArray<int> neighborsOffsets = builder.Construct(ref root.NeighborOffsets, authoring._flowFieldSo.Data.NeighborOffsets);

                AddComponent(dbEntity, new FlowFieldDatabase() { Blobs = spellsDatabaseBlob });

                // Register blob asset (auto free memory)
                AddBlobAsset(ref spellsDatabaseBlob, out var hash);

                // Dispose builder
                builder.Dispose();
            }
        }

        #endregion

        #region Members
        
        private FlowFieldBlob flowFieldBlob;

        #endregion
        
        #region Accessors

        public FlowFieldBlob ActualFlowFieldBlob => flowFieldBlob;

        #endregion

        #region Methods


        #endregion
    }
}