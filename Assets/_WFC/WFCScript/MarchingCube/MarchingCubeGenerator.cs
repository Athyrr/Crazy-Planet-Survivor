using EasyButtons;
using UnityEngine;

namespace WFCContent.MarchingCube
{
    [ExecuteInEditMode]
    public class MarchingCubeGenerator : MonoBehaviour
    {
        [SerializeField, Min(2)] private int resolution = 32;
        [SerializeField] private float isoLevel = 0f;
        [SerializeField] private float radius = 8f;
        [SerializeField] private float cellSize = 0.5f;
        [SerializeField] private Material material;

        private MeshFilter meshFilter;

        private void Awake() => EnsureComponents();

        [Button]
        private void Regenerate()
        {
            EnsureComponents();

            var grid = new float[resolution + 1, resolution + 1, resolution + 1];
            var center = new Vector3(resolution, resolution, resolution) * 0.5f;

            for (var x = 0; x <= resolution; x++)
            {
                for (var y = 0; y <= resolution; y++)
                {
                    for (var z = 0; z <= resolution; z++)
                    {
                        var pos = new Vector3(x, y, z) - center;
                        grid[x, y, z] = pos.magnitude - radius;
                    }
                }
            }

            meshFilter.sharedMesh = MarchingCube.GenerateMesh(grid, isoLevel);
            transform.localScale = Vector3.one * cellSize;

            var renderer = GetComponent<MeshRenderer>();
            if (renderer != null && material != null)
                renderer.sharedMaterial = material;
        }

        [Button]
        private void Clear()
        {
            EnsureComponents();
            meshFilter.sharedMesh = null;
        }

        private void EnsureComponents()
        {
            if (meshFilter == null)
                meshFilter = GetComponent<MeshFilter>() ?? gameObject.AddComponent<MeshFilter>();
            if (GetComponent<MeshRenderer>() == null)
                gameObject.AddComponent<MeshRenderer>();
        }
    }
}
