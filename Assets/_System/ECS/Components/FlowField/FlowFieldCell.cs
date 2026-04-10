using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Buffer element stored on the FlowField singleton entity.
/// Index in the buffer corresponds to: cellY * GridWidth + cellX.
/// </summary>
public struct FlowFieldCell : IBufferElementData
{
    /// <summary>
    /// Normalized world-space direction an entity in this cell should move toward.
    /// float3.zero when the cell is the goal or unreachable.
    /// </summary>
    public float3 Direction;

    /// <summary>
    /// Integration cost from this cell to the goal. byte.MaxValue means blocked / unreachable.
    /// </summary>
    public byte Cost;
}
