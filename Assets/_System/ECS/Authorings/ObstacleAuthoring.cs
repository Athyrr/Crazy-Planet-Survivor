using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class ObstacleAuthoring : MonoBehaviour
{
    [Header("Avoidance Settings")]
    [Tooltip("The repulsion strength applied to enemies.")]
    public float RepulsionWeight = 10f;

    [Header("Size Calculation")]
    [Tooltip("If true, calculates the radius based on the Collider or Renderer.")]
    public bool AutoCalculateRadius = true;

    [Tooltip("Manual radius used if auto-calculation is disabled.")]
    public float ManualRadius = 1.0f;

    [Tooltip("Additional safety margin added to the calculated radius.")]
    public float Padding = 0.0f;

    [Header("Debug Info (Read Only)")]
    [SerializeField]
    [Tooltip("Current calculated radius value.")]
    private float _currentRadiusDisplay;

    private void OnValidate()
    {
        _currentRadiusDisplay = GetRadius();
    }

    private void OnDrawGizmosSelected()
    {
        // Update the display here as well so it reacts in real-time 
        // if you change the object's Scale in the scene
        _currentRadiusDisplay = GetRadius();

        Gizmos.color = new Color(1f, 0f, 0f, 0.5f); 
        Gizmos.DrawWireSphere(transform.position, _currentRadiusDisplay);
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