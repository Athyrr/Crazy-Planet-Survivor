using UnityEngine;

namespace _System.ECS.Components.Flowfield
{
    /// <summary>
    /// we use SO to create persistant data per planet information
    /// </summary>
    [CreateAssetMenu(menuName = "FlowField/FlowFieldData")]
    public class FlowFieldSO : ScriptableObject
    {
        public FlowFieldData Data;
    }
}