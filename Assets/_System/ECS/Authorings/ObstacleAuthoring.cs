using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class ObstacleAuthoring : MonoBehaviour
{
    [Header("Avoidance Settings")]
    [Tooltip("The repulsion strength applied to enemies.")]
    [SerializeField]
    public float RepulsionWeight = 10f;

    [Header("Size Calculation")]
    [Tooltip("If true, calculates the radius based on the Collider or Renderer.")]
    [SerializeField]
    public bool AutoCalculateRadius = true;

    [Tooltip("Manual radius used if auto-calculation is disabled.")]
    [SerializeField]
    public float ManualRadius = 1.0f;

    [Tooltip("Additional safety margin added to the calculated radius.")]
    [SerializeField]
    public float Padding = 0.0f;

    [Header("Debug Info (Read Only)")]
    [SerializeField]
    [Tooltip("Current calculated radius value.")]
    private float _currentRadiusDisplay;

    [SerializeField] private bool _debug;

    private void OnValidate()
    {
        _currentRadiusDisplay = GetRadius();
    }

    private void OnDrawGizmosSelected()
    {
        if (!_debug)
            return;
        
        // Update the display here as well so it reacts in real-time 
        // if you change the object's Scale in the scene
        _currentRadiusDisplay = GetRadius();

        Gizmos.color = Color.aquamarine;
        Gizmos.DrawSphere(transform.position, _currentRadiusDisplay);
    }

    /// <summary>
    /// Calculates the radius based on the collider or mesh.
    /// </summary>
    public float GetRadius()
    {
        if (!AutoCalculateRadius) return ManualRadius;

        var col = GetComponent<Collider>();
        if (col != null)
        {
            float maxExtent = Mathf.Max(col.bounds.extents.x, col.bounds.extents.z);
            return maxExtent + Padding;
        }

        var rend = GetComponent<Renderer>();
        if (rend != null)
        {
            float maxExtent = Mathf.Max(rend.bounds.extents.x, rend.bounds.extents.z);
            return maxExtent + Padding;
        }

        return ManualRadius;
    }

    private class Baker : Baker<ObstacleAuthoring>
    {
        public override void Bake(ObstacleAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Renderable | TransformUsageFlags.WorldSpace);

            float radius = authoring.GetRadius();

            AddComponent(entity, new Obstacle
            {
                AvoidanceRadius = radius,
                Weight = authoring.RepulsionWeight
            });
        }
    }
}