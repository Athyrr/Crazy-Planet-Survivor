using Unity.Entities;
using UnityEngine;

/// <summary>
/// Authoring component for the FlowField singleton.
/// Place this on a dedicated GameObject in the scene to initialize the flow field.
/// </summary>
public class FlowFieldDataAuthoring : MonoBehaviour
{
    [Header("Grid Settings")]
    [Tooltip("Number of cells along the X axis.")]
    [SerializeField] private int _gridWidth = 64;

    [Tooltip("Number of cells along the Z axis.")]
    [SerializeField] private int _gridHeight = 64;

    [Tooltip("World-space size of each grid cell.")]
    [SerializeField] private float _cellSize = 2.5f;

    [Header("Rebuild Settings")]
    [Tooltip("How often (in seconds) the flow field is fully recomputed.")]
    [SerializeField] private float _rebuildInterval = 0.2f;

    private class Baker : Baker<FlowFieldDataAuthoring>
    {
        public override void Bake(FlowFieldDataAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);

            AddComponent(entity, new FlowFieldData
            {
                GridWidth = authoring._gridWidth,
                GridHeight = authoring._gridHeight,
                CellSize = authoring._cellSize,
                RebuildInterval = authoring._rebuildInterval,
                IsReady = false,
                TimeSinceLastRebuild = float.MaxValue // force an immediate rebuild on first frame
            });

            // Pre-allocate the cell buffer at bake time
            var buffer = AddBuffer<FlowFieldCell>(entity);
            buffer.ResizeUninitialized(authoring._gridWidth * authoring._gridHeight);
        }
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0f, 1f, 0.5f, 0.4f);
        Gizmos.DrawWireCube(transform.position, new Vector3(_gridWidth * _cellSize, 0.5f, _gridHeight * _cellSize));
    }
}
