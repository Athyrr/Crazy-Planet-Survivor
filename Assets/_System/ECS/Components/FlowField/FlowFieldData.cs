using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Singleton component that stores the current state and metadata of the flow field.
/// The actual per-cell direction data lives in a DynamicBuffer&lt;FlowFieldCell&gt; on the same entity.
/// </summary>
public struct FlowFieldData : IComponentData
{
    /// <summary> Number of cells along the local X axis. </summary>
    public int GridWidth;

    /// <summary> Number of cells along the local Z axis. </summary>
    public int GridHeight;

    /// <summary> World-space size of each cell. </summary>
    public float CellSize;

    /// <summary> World position of the goal (player) when the field was last rebuilt. </summary>
    public float3 Origin;

    /// <summary> Tangent-plane X axis of the grid, aligned to the planet surface at Origin. </summary>
    public float3 GridRight;

    /// <summary> Tangent-plane Z axis of the grid, aligned to the planet surface at Origin. </summary>
    public float3 GridForward;

    /// <summary> Surface normal at Origin. </summary>
    public float3 GridNormal;

    /// <summary> Whether the field has been computed at least once and is safe to query. </summary>
    public bool IsReady;

    /// <summary> Seconds between full rebuilds. </summary>
    public float RebuildInterval;

    /// <summary> Accumulated time since the last rebuild. </summary>
    public float TimeSinceLastRebuild;
}
