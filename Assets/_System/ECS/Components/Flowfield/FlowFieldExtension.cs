using System.Collections.Generic;
using System.Linq;
using EasyButtons;
using Unity.Mathematics;
using UnityEngine;

namespace _System.ECS.Components.Flowfield
{
    // used only to fill data prebaked. to optimize obstacle detection or some like it (static entity with avoidance)
    // any data used in ECS only to bake data and save in Scriptable Object to load ECS side and load in cache
    // (usefully for complexity logic)
    public class FlowFieldExtension : MonoBehaviour
    {
        [SerializeField] private List<MeshFilter> _excludeMeshRenderers;
        [SerializeField] private FlowFieldSO flowFieldSo;

        #region  Debug
#if UNITY_EDITOR
        [Header("Simulation Debug")]
        [SerializeField] private int _startIndex;
        [SerializeField] private int _targetIndex;
        private float[] _debugDistances;
        private List<int> _debugPath = new List<int>();
#endif
        #endregion


        #region Methods

        
        private void AddNeighbor(List<List<int>> adjacency, int a, int b)
        {
            if (!adjacency[a].Contains(b)) adjacency[a].Add(b);
            if (!adjacency[b].Contains(a)) adjacency[b].Add(a);
        }
        
        #endregion

        #region Debug
#if UNITY_EDITOR

        
        [Button]
        private void BakeFlowField()
        {
            var allMf = GetComponentsInChildren<MeshFilter>()
                .Where(mf => !_excludeMeshRenderers.Contains(mf)).ToArray();

            if (allMf.Length == 0) return;

            // use dic to find with snapped position (mb save Vector3i if possible to reduce float precision error ?)
            Dictionary<Vector3, int> posToId = new Dictionary<Vector3, int>();
            List<Vector3> uniquePositions = new List<Vector3>();
            List<List<int>> adjacency = new List<List<int>>();

            foreach (var mf in allMf)
            {
                var mesh = mf.sharedMesh;
                var verts = mesh.vertices;
                var tris = mesh.triangles;
                int[] meshToGlobal = new int[verts.Length];

                for (int i = 0; i < verts.Length; i++)
                {
                    Vector3 wPos = mf.transform.TransformPoint(verts[i]);
                    
                    // snap to remove float precision error
                    Vector3 snappedPos = new Vector3(
                        Mathf.Round(wPos.x * 1000f) / 1000f,
                        Mathf.Round(wPos.y * 1000f) / 1000f,
                        Mathf.Round(wPos.z * 1000f) / 1000f
                    );

                    if (!posToId.TryGetValue(snappedPos, out int existingId))
                    {
                        existingId = uniquePositions.Count;
                        posToId.Add(snappedPos, existingId);
                        uniquePositions.Add(wPos);
                        adjacency.Add(new List<int>());
                    }
                    meshToGlobal[i] = existingId;
                }

                // manage neighbor link
                for (int i = 0; i < tris.Length; i += 3)
                {
                    int a = meshToGlobal[tris[i]];
                    int b = meshToGlobal[tris[i + 1]];
                    int c = meshToGlobal[tris[i + 2]];
                    AddNeighbor(adjacency, a, b);
                    AddNeighbor(adjacency, b, c);
                    AddNeighbor(adjacency, c, a);
                }
            }

            // transform data into flat index to send into ScriptableObject (Format ECS-Ready, dev friendly)
            int totalCount = uniquePositions.Count;
            List<int> flatNeighbors = new List<int>();
            int[] offsets = new int[totalCount];
            int[] counts = new int[totalCount];

            for (int i = 0; i < totalCount; i++)
            {
                offsets[i] = flatNeighbors.Count;
                counts[i] = adjacency[i].Count;
                flatNeighbors.AddRange(adjacency[i]);
            }

            flowFieldSo.Data.Positions = uniquePositions.ToArray();
            flowFieldSo.Data.Neighbors = flatNeighbors.ToArray();
            flowFieldSo.Data.NeighborOffsets = offsets;
            flowFieldSo.Data.NeighborCounts = counts;

            Debug.Log($"BakeFlowField : {totalCount} total nods linked.");
        }
        
        [Button]
        private void SimulatePath()
        {
            if (flowFieldSo == null || flowFieldSo.Data.Positions == null) return;
            
            int count = flowFieldSo.Data.Positions.Length;
            _debugDistances = new float[count];
            for (int i = 0; i < count; i++) _debugDistances[i] = float.MaxValue;

            // Dijkstra BFS
            Queue<int> queue = new Queue<int>();
            _debugDistances[_targetIndex] = 0;
            queue.Enqueue(_targetIndex);

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                int start = flowFieldSo.Data.NeighborOffsets[current];
                int nCount = flowFieldSo.Data.NeighborCounts[current];

                for (int i = 0; i < nCount; i++)
                {
                    int neighbor = flowFieldSo.Data.Neighbors[start + i];
                    float dist = _debugDistances[current] + Vector3.Distance(flowFieldSo.Data.Positions[current], flowFieldSo.Data.Positions[neighbor]);

                    if (dist < _debugDistances[neighbor])
                    {
                        _debugDistances[neighbor] = dist;
                        queue.Enqueue(neighbor);
                    }
                }
            }

            // Path Tracing
            _debugPath.Clear();
            int currPathNode = _startIndex;
            _debugPath.Add(currPathNode);

            int safety = 0;
            while (currPathNode != _targetIndex && safety < 1000)
            {
                int start = flowFieldSo.Data.NeighborOffsets[currPathNode];
                int nCount = flowFieldSo.Data.NeighborCounts[currPathNode];
                int bestNeighbor = -1;
                float minDist = _debugDistances[currPathNode];

                for (int i = 0; i < nCount; i++)
                {
                    int neighbor = flowFieldSo.Data.Neighbors[start + i];
                    if (_debugDistances[neighbor] < minDist)
                    {
                        minDist = _debugDistances[neighbor];
                        bestNeighbor = neighbor;
                    }
                    Debug.Log($"hyv; exec {i}");
                }

                if (bestNeighbor == -1) break;
                currPathNode = bestNeighbor;
                _debugPath.Add(currPathNode);
                safety++;
            }

            Debug.Log($"hyv; success: {_debugPath.Count}");
        }

        // todo @hyverno bench mark with ECS 
        [Button]
        public void BenchPathfinding()
        {
            int iterations = 100;
            var sw = System.Diagnostics.Stopwatch.StartNew();

            for (int i = 0; i < iterations; i++)
            {
                SimulatePath();
            }

            sw.Stop();
            double averageMs = sw.Elapsed.TotalMilliseconds / iterations;
            Debug.Log($"hyv; {iterations} runs : {averageMs:F4} ms");
        }

        private void OnDrawGizmos()
        {
            if (flowFieldSo == null || flowFieldSo.Data.Positions == null) return;

            // draw path
            if (_debugPath != null && _debugPath.Count > 1)
            {
                Gizmos.color = Color.red;
                for (int i = 0; i < _debugPath.Count - 1; i++)
                {
                    Gizmos.DrawLine(flowFieldSo.Data.Positions[_debugPath[i]], flowFieldSo.Data.Positions[_debugPath[i+1]]);
                    Gizmos.DrawSphere(flowFieldSo.Data.Positions[_debugPath[i]], 0.2f);
                }
            }

            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(flowFieldSo.Data.Positions[_targetIndex], 0.2f);
        }
        
#endif
        #endregion
    }
}
