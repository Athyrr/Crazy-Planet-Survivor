using Unity.Entities;
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
                
                // transit serialized data into ECS
                // authoring._flowFieldSo.Data;
            }
        }

        #endregion

        #region Members
        
        private FlowField flowField;

        #endregion
        
        #region Accessors

        public FlowField ActualFlowField => flowField;

        #endregion

        #region Methods


        #endregion
    }
}