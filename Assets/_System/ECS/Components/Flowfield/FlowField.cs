using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public struct FlowFieldData
{
    [Header("Static Data")]
    public Vector3[] Positions;       // position for each vertex
    
    // flat index
    public int[] Neighbors;           // all neighbors
    public int[] NeighborOffsets;     // first neighbor for each table
    public int[] NeighborCounts;      // neighbor counts for each vertex
    
    public int VertexCount => Positions.Length;
}

// same as FlowFieldData for ECS
// required but Blob can't serialize (in editor)
public struct FlowField
{
    // Static Data
    public BlobArray<float3> Positions;       // position for each vertex
    
    // flat index
    public BlobArray<int> Neighbors;           // all neighbors
    public BlobArray<int> NeighborOffsets;     // first neighbor for each table
    public BlobArray<int> NeighborCounts;      // neighbor counts for each vertex
    
    public int VertexCount => Positions.Length;
}
