using UnityEngine;

namespace _System.ECS.Components.Flowfield
{
    /// <summary>
    /// we use SO to create persistant data per planet information
    /// </summary>
    [CreateAssetMenu(menuName = "FlowField/FlowFieldData")]
    public class FlowFieldData: ScriptableObject
    {
        public FlowFieldExtension Prefab; // planet reference
        public FlowFieldType[] FlowFieldTypes = new FlowFieldType[0];
    }
}