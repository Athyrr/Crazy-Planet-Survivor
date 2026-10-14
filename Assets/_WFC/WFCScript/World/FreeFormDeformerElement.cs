using System.Collections.Generic;
using UnityEngine;
using WFCContent.HE;

namespace WFCContent.World
{
    public class FreeFormDeformerElement : MonoBehaviour
    {
        [SerializeField] public List<Vector3> Anchors = new ();

        [SerializeField] private bool overrideRotation;
        [SerializeField] private bool overridePosition;
        [SerializeField, Tooltip("Fit the triangular cage to the mesh vertices instead of centering it on the pivot.")]
        private bool useTopCenter;
        
        [SerializeField, ShowIf(nameof(overridePosition))]
        private Vector3 triangleCenterOffset = Vector3.zero;
        
        [SerializeField, ShowIf(nameof(overrideRotation))]
        private float triangleAngleDegrees = 0f;
        
        public List<Vector3> GetRelativeAnchors()
        {
            var result = new List<Vector3>(Anchors.Count);
            for (int i = 0; i < Anchors.Count; i++)
                result.Add(transform.TransformPoint(Anchors[i]));
            return result;
        }
        
        public Vector3 GetWorldPosition(Vector3 localVertexPosition) => transform.TransformPoint(localVertexPosition);

        [SerializeField] private List<Vector3> debugTrianglesCompute = new List<Vector3>();
        
        private void OnDrawGizmosSelected()
        {
            foreach(var v in GetRelativeAnchors())
                Gizmos.DrawWireCube(v, Vector3.one * 0.02f);

            for (int i = 0; i < debugTrianglesCompute.Count; i++)
            {
                var el = transform.TransformPoint(debugTrianglesCompute[i]);
                Gizmos.color = Color.red;
                Gizmos.DrawWireCube(el, Vector3.one * 0.05f);
            }
        }

        // fonctionne uniquement si le Goldberg Polyhedron a comme valuer m == n (class II du polyhedre)
        public void ComputeTriangulatedAnchors()
        {
            var mf = GetComponentInChildren<MeshFilter>();
            if (mf == null || mf.sharedMesh == null)
                return;

            var mesh = mf.sharedMesh;
            var bounds = mesh.bounds;
            var vertices = mesh.vertices;
            if (vertices.Length == 0)
                return;

            float angleDegrees = overrideRotation ? triangleAngleDegrees : 0f;
            FitTriangle(vertices, angleDegrees, useTopCenter, out Vector2 triangleCenter, out float radius);
            var center = new Vector3(triangleCenter.x, triangleCenter.y,
                useTopCenter ? bounds.center.z : 0f);
            
            if (overridePosition)
                center += triangleCenterOffset;
            
            var extents = mesh.bounds.extents;

            var triangle = new List<Vector2>();
            for (int i = 0; i < 3; i++)
            {
                float angle = ((i * 120f) + angleDegrees) * Mathf.Deg2Rad;
                var res = new Vector2(
                    center.x + Mathf.Cos(angle) * radius,
                    center.y + Mathf.Sin(angle) * radius
                );
                triangle.Add(res);
            }

            Anchors.Clear();
            debugTrianglesCompute.Clear();

            for (int z = 0; z < 2; z++)
            {
                float baseDepth = useTopCenter ? center.z - extents.z : center.z;
                float depth = baseDepth + z * extents.z * 2;

                for (int i = 0; i < triangle.Count; i++)
                {
                    Vector3 point = new Vector3(triangle[i].x, triangle[i].y, depth);
                    Vector3 worldPoint = mf.transform.TransformPoint(point);
                    Anchors.Add(transform.InverseTransformPoint(worldPoint));
                }
            }

            debugTrianglesCompute = new List<Vector3>(Anchors);
        }

        // hack tricks (je suis pas tres bon en math alors hop)
        private static void FitTriangle(Vector3[] vertices, float angleDegrees, bool fitCenter,
            out Vector2 center, out float radius)
        {
            center = Vector2.zero;
            radius = 0f;
            for (int i = 0; i < 3; i++)
            {
                float angle = (angleDegrees + i * 120f) * Mathf.Deg2Rad;
                var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                float support = float.PositiveInfinity;
                foreach (var vertex in vertices)
                    support = Mathf.Min(support, direction.x * vertex.x + direction.y * vertex.y);

                if (fitCenter)
                {
                    // Each opposite edge satisfies dot(direction, center) - radius / 2 = support.
                    center += direction * (2f / 3f * support);
                    radius -= 2f / 3f * support;
                }
                else
                {
                    radius = Mathf.Max(radius, -2f * support);
                }
            }
        }

    }
}
