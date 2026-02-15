using Unity.Entities;
using UnityEngine;

namespace _System.ECS.Components.Flowfield
{
    public class FlowFieldAuthoring: MonoBehaviour
    {
        #region ECS
        
        private class Baker : Baker<FlowFieldAuthoring>
        {
            public override void Bake(FlowFieldAuthoring authoring)
            {
                
            }
        }

        #endregion

        #region Members
        
        private FlowFieldData _flowFieldData;

        #endregion
        
        #region Accessors

        public FlowFieldData ActualFlowFieldData => _flowFieldData;

        #endregion

        #region Bake FlowfieldData

        

        #endregion

        #region Methods

        public void SetFlowFieldData(FlowFieldData flowFieldData) => _flowFieldData = flowFieldData;

        #endregion
    }
}