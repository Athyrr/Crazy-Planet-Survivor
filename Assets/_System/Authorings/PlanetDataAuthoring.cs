using Unity.Entities;
using UnityEngine;

public class PlanetDataAuthoring : MonoBehaviour
{
    private float _radius = 1f;

    public float Radius => _radius;

    private class Baker : Baker<PlanetDataAuthoring>
    {

        public override void Bake(PlanetDataAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent(entity, new PlanetData()
            {
                Radius = authoring.Radius
            });
        }
    }

    private void OnValidate()
    {
        var mesh = GetComponent<MeshFilter>();
        if (mesh && mesh.sharedMesh)
            _radius = 0.5f * mesh.sharedMesh.bounds.size.x * transform.lossyScale.x;
        //_radius = 25f;

        Debug.Log("Radius: " + _radius);
    }
}
