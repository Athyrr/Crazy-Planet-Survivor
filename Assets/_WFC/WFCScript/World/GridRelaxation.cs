using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace WFCContent.World
{
    // grille organique facon Townscaper (Oskar Stalberg), sur une sphere de triangles:
    // 1) des aretes sont retournees au hasard: les sommets ont 5, 6 ou 7 triangles, le motif regulier disparait
    // 2) relaxation: chaque triangle propose le triangle equilateral qui lui ressemble le plus (meme centre, meme taille),
    //    chaque sommet se deplace vers la moyenne des positions proposees par ses triangles, puis revient sur la sphere
    // la topologie reste une triangulation de la sphere (voisins par arete partagee): le WFC n'a rien a changer
    public static class GridRelaxation
    {
        private const int MinValence = 5;
        private const int MaxValence = 7;

        public static void FlipEdges(List<Vector3> points, List<int[]> triangles, float amount, System.Random rng)
        {
            var edgeTriangles = new Dictionary<(int, int), List<int>>();
            for (int t = 0; t < triangles.Count; t++)
                Link(edgeTriangles, triangles, t);

            var valence = new int[points.Count];
            foreach (var triangle in triangles)
                foreach (int v in triangle)
                    valence[v]++;

            // tous les triangles tournent dans le meme sens: on garde celui du premier
            float winding = Mathf.Sign(Orientation(points, triangles[0][0], triangles[0][1], triangles[0][2]));
            int attempts = Mathf.RoundToInt(amount * triangles.Count);
            for (int i = 0; i < attempts; i++)
            {
                int t1 = rng.Next(triangles.Count);
                int e = rng.Next(3);
                int p = triangles[t1][e], q = triangles[t1][(e + 1) % 3], r = triangles[t1][(e + 2) % 3];
                var shared = edgeTriangles[Key(p, q)];
                if (shared.Count != 2)
                    continue;
                int t2 = shared[0] == t1 ? shared[1] : shared[0];
                int s = triangles[t2].First(v => v != p && v != q);

                // p et q perdent un triangle, r et s en gagnent un
                if (r == s || valence[p] <= MinValence || valence[q] <= MinValence ||
                    valence[r] >= MaxValence || valence[s] >= MaxValence)
                    continue;
                if (edgeTriangles.TryGetValue(Key(r, s), out var existing) && existing.Count > 0)
                    continue;
                // quadrilatere p-s-q-r convexe seulement, sinon un des nouveaux triangles serait retourne
                if (Orientation(points, p, s, r) * winding <= 0f || Orientation(points, s, q, r) * winding <= 0f)
                    continue;

                Unlink(edgeTriangles, triangles, t1);
                Unlink(edgeTriangles, triangles, t2);
                triangles[t1] = new[] { p, s, r };
                triangles[t2] = new[] { s, q, r };
                Link(edgeTriangles, triangles, t1);
                Link(edgeTriangles, triangles, t2);
                valence[p]--;
                valence[q]--;
                valence[r]++;
                valence[s]++;
            }
        }

        // sizeEqualization: 0 = chaque triangle garde sa taille (Townscaper), 1 = tous visent la taille moyenne.
        // les retournements d'aretes font varier les tailles: sans egalisation les stamps changent beaucoup d'echelle
        public static void Relax(List<Vector3> points, List<int[]> triangles, int iterations, float strength, float sizeEqualization)
        {
            float sphereRadius = points.Average(p => p.magnitude);
            var offsets = new Vector3[points.Count];
            var counts = new int[points.Count];
            var targets = new Vector3[3];
            float meanSize = -1f;

            for (int it = 0; it < iterations; it++)
            {
                System.Array.Clear(offsets, 0, offsets.Length);
                System.Array.Clear(counts, 0, counts.Length);
                float sizeSum = 0f;

                foreach (var triangle in triangles)
                {
                    Vector3 center = (points[triangle[0]] + points[triangle[1]] + points[triangle[2]]) / 3f;
                    Vector3 normal = center.normalized;
                    Vector3 v0 = Vector3.ProjectOnPlane(points[triangle[0]] - center, normal);
                    Vector3 v1 = Vector3.ProjectOnPlane(points[triangle[1]] - center, normal);
                    Vector3 v2 = Vector3.ProjectOnPlane(points[triangle[2]] - center, normal);

                    // on ramene v1 et v2 sur v0 par une rotation de 120/240 degres puis on moyenne:
                    // la bonne orientation est celle ou les trois vecteurs s'alignent (moyenne la plus longue)
                    var r120 = Quaternion.AngleAxis(120f, normal);
                    var r240 = Quaternion.AngleAxis(240f, normal);
                    Vector3 senseA = (v0 + r240 * v1 + r120 * v2) / 3f;
                    Vector3 senseB = (v0 + r120 * v1 + r240 * v2) / 3f;
                    bool useA = senseA.sqrMagnitude >= senseB.sqrMagnitude;
                    Vector3 d = useA ? senseA : senseB;
                    float size = d.magnitude;
                    sizeSum += size;
                    if (meanSize > 0f && size > 1e-6f)
                        d *= 1f - sizeEqualization + sizeEqualization * meanSize / size;

                    targets[0] = center + d;
                    targets[1] = center + (useA ? r120 : r240) * d;
                    targets[2] = center + (useA ? r240 : r120) * d;
                    for (int k = 0; k < 3; k++)
                    {
                        offsets[triangle[k]] += targets[k] - points[triangle[k]];
                        counts[triangle[k]]++;
                    }
                }

                meanSize = sizeSum / triangles.Count;
                for (int i = 0; i < points.Count; i++)
                    if (counts[i] > 0)
                        points[i] = (points[i] + offsets[i] / counts[i] * strength).normalized * sphereRadius;
            }
        }

        // > 0 si (a, b, c) tourne dans le sens direct vu de l'exterieur de la sphere
        private static float Orientation(List<Vector3> points, int a, int b, int c) =>
            Vector3.Dot(Vector3.Cross(points[b] - points[a], points[c] - points[a]), points[a] + points[b] + points[c]);

        private static (int, int) Key(int a, int b) => a < b ? (a, b) : (b, a);

        private static void Link(Dictionary<(int, int), List<int>> edges, List<int[]> triangles, int t)
        {
            var triangle = triangles[t];
            for (int e = 0; e < 3; e++)
            {
                var key = Key(triangle[e], triangle[(e + 1) % 3]);
                if (!edges.TryGetValue(key, out var list))
                    edges[key] = list = new List<int>(2);
                list.Add(t);
            }
        }

        private static void Unlink(Dictionary<(int, int), List<int>> edges, List<int[]> triangles, int t)
        {
            var triangle = triangles[t];
            for (int e = 0; e < 3; e++)
                edges[Key(triangle[e], triangle[(e + 1) % 3])].Remove(t);
        }
    }
}
