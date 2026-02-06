using Unity.Entities;
using UnityEngine;

public class PlanetDataAuthoring : MonoBehaviour
{
    [Header("Planet")]

    [Tooltip("Planet ID")]
    [SerializeField]
    private EPlanetID _planetID = EPlanetID.None;

    [Tooltip("Planet radius")]
    [SerializeField]
    private float _radius = 50f;

    [SerializeField]
    private bool _autoFindPlanetRadius;

    [Header("Run")]
    [SerializeField]
    [Tooltip("Duration of the run in seconds")]
    private float _runDuration = 60f;

    private class Baker : Baker<PlanetDataAuthoring>
    {
        public override void Bake(PlanetDataAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.None);

            AddComponent(entity, new PlanetData()
            {
                PlanetID = authoring._planetID,
                Center = authoring.transform.position,
                Radius = authoring._radius,
                RunDuration = authoring._runDuration
            });
        }
    }

    private void OnValidate()
    {
        if (_autoFindPlanetRadius)
        {
            var mesh = GetComponent<MeshFilter>();
            if (mesh && mesh.sharedMesh)
                _radius = 0.5f * mesh.sharedMesh.bounds.size.x * transform.lossyScale.x;
        }
    }
}