using System.Collections.Generic;
using System.Linq;
using EasyButtons;
using UnityEngine;

namespace _System.ECS.Components.Flowfield
{
    /// <summary>
    /// place this on Planet ref to setup custom flowfield params. like void location or some other
    /// </summary>
    public class FlowFieldExtension: MonoBehaviour
    {
        [SerializeField] private List<MeshFilter> _excludeMeshRenderers;
        [SerializeField] private FlowFieldData _flowFieldData; 
        
        #region Editor
#if UNITY_EDITOR

        [Button]
        private void BakeFlowField()
        {
            var allMf = GetComponentsInChildren<MeshFilter>();
            if (allMf.Length == 0)
            {
                Debug.LogError("No MeshFilter found");
                return;
            }

            int vc = allMf
                .Where(mf => !_excludeMeshRenderers.Contains(mf))
                .Sum(mf => mf.sharedMesh.vertexCount);

            _flowFieldData.FlowFieldTypes = new FlowFieldType[vc];

            List<int>[] adjacency = new List<int>[vc];
            for (int i = 0; i < vc; i++)
                adjacency[i] = new List<int>();

            int globalOffset = 0;

            foreach (var mf in allMf)
            {
                if (_excludeMeshRenderers.Contains(mf))
                    continue;

                var mesh = mf.sharedMesh;
                var verts = mesh.vertices;
                var tris = mesh.triangles;

                // Fill vertices
                for (int j = 0; j < verts.Length; j++)
                {
                    Vector3 worldPos = mf.transform.TransformPoint(verts[j]);
                    _flowFieldData.FlowFieldTypes[globalOffset + j] = new FlowFieldType(worldPos, Vector3.zero);
                }

                // Build adjacency
                for (int j = 0; j < tris.Length; j += 3)
                {
                    int a = globalOffset + tris[j];
                    int b = globalOffset + tris[j + 1];
                    int c = globalOffset + tris[j + 2];

                    AddNeighbor(adjacency, a, b);
                    AddNeighbor(adjacency, a, c);
                    AddNeighbor(adjacency, b, c);
                }

                globalOffset += verts.Length;
            }
            
            // Compute direction
            for (int i = 0; i < _flowFieldData.FlowFieldTypes.Length; i++)
            {
                var neighbors = adjacency[i];
                if (neighbors.Count == 0)
                {
                    _flowFieldData.FlowFieldTypes[i] = new FlowFieldType(Vector3.zero, Vector3.zero);
                    continue;
                }

                Vector3 sumDir = Vector3.zero;
                Vector3 currentPos = _flowFieldData.FlowFieldTypes[i].Position;

                for (int j = 0; j < neighbors.Count; j++)
                {
                    Vector3 neighborPos = _flowFieldData.FlowFieldTypes[neighbors[j]].Position;
                    sumDir += (neighborPos - currentPos).normalized;
                }

                _flowFieldData.FlowFieldTypes[i] = new FlowFieldType(currentPos, sumDir.normalized);
            }

            _flowFieldData.FlowFieldTypes = _flowFieldData.FlowFieldTypes
                .Where(el => el.Position != Vector3.zero).ToArray();

            Debug.Log("FlowField baked: " + _flowFieldData.FlowFieldTypes.Length + " vertices");
        }

        
        private void AddNeighbor(List<int>[] adjacency, int a, int b)
        {
            if (!adjacency[a].Contains(b))
                adjacency[a].Add(b);

            if (!adjacency[b].Contains(a))
                adjacency[b].Add(a);
        }
        
        // draw debug
        private void OnDrawGizmos()
        {
            if (_flowFieldData == null) return;
            for (int i = 0; i < _flowFieldData.FlowFieldTypes.Length; i++)
            {
                Gizmos.color = new Color(_flowFieldData.FlowFieldTypes[i].Position.x * 0.1f % 1, _flowFieldData.FlowFieldTypes[i].Position.y * 0.1f % 1, _flowFieldData.FlowFieldTypes[i].Position.z * 0.1f % 1);
                var pos = _flowFieldData.FlowFieldTypes[i].Position;
                Gizmos.DrawLine(pos, pos + _flowFieldData.FlowFieldTypes[i].Forward * 0.2f);
            }
        }
#endif
        #endregion
    }
}