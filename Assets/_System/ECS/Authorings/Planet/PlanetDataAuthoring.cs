using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class PlanetDataAuthoring : MonoBehaviour
{
    [Header("Planet Identity")]
    [Tooltip("Planet ID")]
    [SerializeField] private EPlanetID _planetID = EPlanetID.None;

    [Header("Auto-Detection")]
    [SerializeField] private bool _autoCalculate = true;

    [SerializeField] private Renderer _planetRenderer;

    [Header("Manual Settings (if Auto is off)")]
    [SerializeField] private float _manualRadius = 50f;
    [SerializeField] private Vector3 _manualCenterOffset = Vector3.zero;

    [Header("Run Settings")]
    [SerializeField] private float _runDuration = 60f;

    private float _finalRadius => _autoCalculate ? CalculateWorldRadius() : _manualRadius;
    private float3 _finalCenter => _autoCalculate ? CalculateWorldCenter() : transform.position + _manualCenterOffset;

    private class Baker : Baker<PlanetDataAuthoring>
    {
        public override void Bake(PlanetDataAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);

            float radius = authoring._manualRadius;
            float3 center = authoring.transform.position + authoring._manualCenterOffset;

            if (authoring._autoCalculate && authoring._planetRenderer != null)
            {
                radius = authoring.CalculateWorldRadius();
                center = authoring.CalculateWorldCenter();
            }

            AddComponent(entity, new PlanetData
            {
                PlanetID = authoring._planetID,
                Center = center,
                Radius = radius,
                RunDuration = authoring._runDuration
            });
        }
    }

    public float CalculateWorldRadius()
    {
        if (_planetRenderer == null)
            return 50f;

        var r = Mathf.Max(_planetRenderer.bounds.extents.x, _planetRenderer.bounds.extents.y, _planetRenderer.bounds.extents.z);
        //Debug.LogWarning("Planet Radius:" + r);
        return r;
    }

    public float3 CalculateWorldCenter()
    {
        if (_planetRenderer == null)
            return transform.position;
        return _planetRenderer.bounds.center;
    }

    private void OnDrawGizmos()
    {
        if (_planetRenderer == null) _planetRenderer = GetComponentInChildren<Renderer>();

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(_finalCenter, 1f);

        Gizmos.color = new Color(1, 1, 0, 0.3f);
        Gizmos.DrawWireSphere(_finalCenter, _finalRadius);

        Gizmos.color = Color.red;
        float3 spawnPoint = _finalCenter + (math.up() * (_finalRadius + 1f));
        Gizmos.DrawSphere(spawnPoint, 2f);
        Gizmos.DrawLine(_finalCenter, spawnPoint);
    }
}