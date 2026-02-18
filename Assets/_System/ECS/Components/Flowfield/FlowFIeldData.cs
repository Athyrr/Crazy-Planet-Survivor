using UnityEngine;

namespace _System.ECS.Components.Flowfield
{
    /// <summary>
    /// we use SO to create persistant data per planet information
    /// </summary>
    [CreateAssetMenu(menuName = "FlowField/FlowFieldData")]
    public class FlowFieldData : ScriptableObject
    {
        [Header("Static Data")]
        public Vector3[] Positions;       // position for each vertex
    
        // flat index
        public int[] Neighbors;           // all neighbors
        public int[] NeighborOffsets;     // first neighbor for each table
        public int[] NeighborCounts;      // neighbor counts for each vertex
    
        public int VertexCount => Positions.Length;
    }
}