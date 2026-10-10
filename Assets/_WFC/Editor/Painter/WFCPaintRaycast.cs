using System.Collections.Generic;
using UnityEngine;
using WFCContent.World;

namespace Editor
{
    public struct PaintHit
    {
        public Vector3 point;     // monde
        public Vector3 normal;    // monde
        public Vector3 direction; // repere de la planete, normalisee
        public int slot;
        public int vertex;
    }

    // rayon de la souris contre le sol des stamps (pas le decor) et le niveau 0, sans collider:
    // on ne teste que les slots que le rayon survole entre le haut des cages et la mer
    public static class WFCPaintRaycast
    {
        private static readonly Dictionary<Mesh, (Vector3[] vertices, int[] triangles)> meshCache = new();

        // les stamps reconstruits ont de nouveaux meshes
        public static void ClearCache() => meshCache.Clear();

        public static bool Raycast(GoldbergPolyhedron planet, Ray worldRay, Dictionary<int, WFCStampInstance> instances, out PaintHit hit)
        {
            hit = default;
            var grid = planet.Grid;
            var toPlanet = planet.transform.worldToLocalMatrix;
            Vector3 origin = toPlanet.MultiplyPoint3x4(worldRay.origin);
            Vector3 direction = toPlanet.MultiplyVector(worldRay.direction);

            // haut des cages: etages + montagnes, avec de la marge
            float outer = grid.Radius * (1f + planet.LayerHeight * (1f + planet.Floors * planet.FloorHeight)) * 1.05f;
            float sea = planet.SeaRadius;
            if (!IntersectSphere(origin, direction, outer, out float tEnter, out float tExit))
                return false;
            tEnter = Mathf.Max(0f, tEnter);
            float tEnd = IntersectSphere(origin, direction, sea, out float tSea, out _) && tSea > 0f ? tSea : tExit;
            if (tEnd <= tEnter)
                return false;

            // slots survoles (plus leurs voisins): echantillons le long du rayon, assez serres pour ne sauter aucun slot
            var candidates = new HashSet<int>();
            Vector3 first = (origin + direction * tEnter).normalized;
            Vector3 last = (origin + direction * tEnd).normalized;
            float edgeAngle = grid.MeanEdge / grid.Radius;
            int samples = Mathf.Clamp(Mathf.CeilToInt(Vector3.Angle(first, last) * Mathf.Deg2Rad / (edgeAngle * 0.35f)), 1, 96);
            for (int i = 0; i <= samples; i++)
            {
                float t = Mathf.Lerp(tEnter, tEnd, i / (float)samples);
                int slot = grid.SlotAt((origin + direction * t).normalized);
                if (slot < 0)
                    continue;
                candidates.Add(slot);
                foreach (int n in grid.SlotNeighbors[slot])
                    candidates.Add(n);
            }

            float best = float.PositiveInfinity;
            Vector3 bestNormal = Vector3.zero;
            foreach (int slot in candidates)
            {
                if (!instances.TryGetValue(slot, out var tag) || tag == null)
                    continue;
                foreach (var filter in tag.GetComponentsInChildren<MeshFilter>())
                {
                    if (filter.name == GoldbergPolyhedron.DecorChildName || filter.sharedMesh == null)
                        continue;
                    if (RaycastMesh(filter, worldRay, ref best, out var normal))
                        bestNormal = normal;
                }
            }

            // pas de sol plus pres: le niveau 0
            if (float.IsInfinity(best))
            {
                if (tEnd != tSea || tSea <= 0f)
                    return false;
                best = tSea;
                bestNormal = planet.transform.TransformDirection((origin + direction * tSea).normalized);
            }

            hit.point = worldRay.GetPoint(best);
            hit.normal = bestNormal.normalized;
            hit.direction = toPlanet.MultiplyPoint3x4(hit.point).normalized;
            hit.slot = grid.SlotAt(hit.direction);
            hit.vertex = grid.NearestVertex(hit.direction);
            return hit.slot >= 0 && hit.vertex >= 0;
        }

        // t est le parametre du rayon monde (le rayon est ramene dans le repere du mesh sans etre normalise)
        private static bool RaycastMesh(MeshFilter filter, Ray worldRay, ref float best, out Vector3 worldNormal)
        {
            worldNormal = Vector3.zero;
            var mesh = filter.sharedMesh;
            if (!meshCache.TryGetValue(mesh, out var data))
            {
                if (meshCache.Count > 4096)
                    meshCache.Clear();
                data = (mesh.vertices, mesh.triangles);
                meshCache[mesh] = data;
            }

            var toLocal = filter.transform.worldToLocalMatrix;
            Vector3 o = toLocal.MultiplyPoint3x4(worldRay.origin);
            Vector3 d = toLocal.MultiplyVector(worldRay.direction);

            // bornes du mesh d'abord
            if (!mesh.bounds.IntersectRay(new Ray(o, d)))
                return false;

            bool found = false;
            var vertices = data.vertices;
            var triangles = data.triangles;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector3 a = vertices[triangles[i]], b = vertices[triangles[i + 1]], c = vertices[triangles[i + 2]];
                if (!IntersectTriangle(o, d, a, b, c, out float t) || t >= best)
                    continue;
                best = t;
                found = true;
                worldNormal = filter.transform.TransformDirection(Vector3.Cross(b - a, c - a));
                if (Vector3.Dot(worldNormal, worldRay.direction) > 0f)
                    worldNormal = -worldNormal;
            }
            return found;
        }

        // Moller-Trumbore, les deux faces
        private static bool IntersectTriangle(Vector3 o, Vector3 d, Vector3 a, Vector3 b, Vector3 c, out float t)
        {
            t = 0f;
            Vector3 e1 = b - a, e2 = c - a;
            Vector3 p = Vector3.Cross(d, e2);
            float det = Vector3.Dot(e1, p);
            if (Mathf.Abs(det) < 1e-12f)
                return false;
            float inv = 1f / det;
            Vector3 s = o - a;
            float u = Vector3.Dot(s, p) * inv;
            if (u < 0f || u > 1f)
                return false;
            Vector3 q = Vector3.Cross(s, e1);
            float v = Vector3.Dot(d, q) * inv;
            if (v < 0f || u + v > 1f)
                return false;
            t = Vector3.Dot(e2, q) * inv;
            return t > 0f;
        }

        // origin + t * direction sur la sphere de centre 0 (direction non normalisee)
        private static bool IntersectSphere(Vector3 origin, Vector3 direction, float radius, out float tEnter, out float tExit)
        {
            float a = Vector3.Dot(direction, direction);
            float b = 2f * Vector3.Dot(origin, direction);
            float c = Vector3.Dot(origin, origin) - radius * radius;
            float disc = b * b - 4f * a * c;
            tEnter = tExit = 0f;
            if (disc < 0f || a <= 0f)
                return false;
            float sq = Mathf.Sqrt(disc);
            tEnter = (-b - sq) / (2f * a);
            tExit = (-b + sq) / (2f * a);
            return tExit > 0f;
        }
    }
}
