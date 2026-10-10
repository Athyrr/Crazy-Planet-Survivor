using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace WFCContent.World
{
    // grille de la planete (repere local): sommets, slots (triangles [o, a, b]) et voisinages.
    // reconstruite a l'identique a partir des reglages de la grille (GoldbergPolyhedron.BuildGrid), jamais serialisee
    public class PlanetGrid
    {
        public readonly List<Vector3> Points;
        public readonly List<int[]> Triangles;
        public readonly List<WFCSlot> Slots;
        // slots qui partagent une arete (regles du WFC)
        public readonly List<List<int>> SlotNeighbors;
        // slots qui touchent chaque sommet
        public readonly List<int>[] VertexSlots;
        // sommets relies par une arete
        public readonly List<int>[] VertexNeighbors;
        public readonly float Radius;
        public readonly float MeanEdge;
        public int Signature;

        public int SlotCount => Slots.Count;
        public int VertexCount => Points.Count;

        // orientation de chaque slot vue de l'exterieur (+1 / -1), pour savoir quel slot contient une direction
        private readonly float[] slotWinding;

        public PlanetGrid(List<Vector3> points, List<int[]> triangles)
        {
            Points = points;
            Triangles = triangles;
            Slots = new List<WFCSlot>(triangles.Count);
            SlotNeighbors = new List<List<int>>(triangles.Count);
            VertexSlots = new List<int>[points.Count];
            VertexNeighbors = new List<int>[points.Count];
            for (int v = 0; v < points.Count; v++)
            {
                VertexSlots[v] = new List<int>(7);
                VertexNeighbors[v] = new List<int>(7);
            }
            slotWinding = new float[triangles.Count];

            // voisins par arete partagee, dans l'ordre historique de GoldbergPolyhedron (le WFC en depend: memes planetes)
            var edgeSlot = new Dictionary<(int, int), int>();
            float edgeSum = 0f;
            for (int i = 0; i < triangles.Count; i++)
            {
                var t = triangles[i];
                Slots.Add(new WFCSlot { o = t[0], a = t[1], b = t[2], vertexIds = t, center = (points[t[0]] + points[t[1]] + points[t[2]]) / 3f });
                SlotNeighbors.Add(new List<int>(3));
                slotWinding[i] = Mathf.Sign(Vector3.Dot(Vector3.Cross(points[t[1]] - points[t[0]], points[t[2]] - points[t[0]]),
                    points[t[0]] + points[t[1]] + points[t[2]]));

                for (int e = 0; e < 3; e++)
                {
                    VertexSlots[t[e]].Add(i);
                    var edge = (Mathf.Min(t[e], t[(e + 1) % 3]), Mathf.Max(t[e], t[(e + 1) % 3]));
                    if (edgeSlot.TryGetValue(edge, out int other))
                    {
                        SlotNeighbors[i].Add(other);
                        SlotNeighbors[other].Add(i);
                    }
                    else
                    {
                        edgeSlot[edge] = i;
                        VertexNeighbors[edge.Item1].Add(edge.Item2);
                        VertexNeighbors[edge.Item2].Add(edge.Item1);
                        edgeSum += (points[edge.Item1] - points[edge.Item2]).magnitude;
                    }
                }
            }

            Radius = points.Count > 0 ? points.Average(p => p.magnitude) : 1f;
            MeanEdge = edgeSlot.Count > 0 ? edgeSum / edgeSlot.Count : Radius * 0.1f;
        }

        #region Recherche

        // sommet le plus proche d'une direction (repere local de la planete)
        public int NearestVertex(Vector3 direction)
        {
            int best = -1;
            float bestDot = float.NegativeInfinity;
            for (int v = 0; v < Points.Count; v++)
            {
                float d = Vector3.Dot(Points[v], direction);
                if (d > bestDot)
                {
                    bestDot = d;
                    best = v;
                }
            }
            return best;
        }

        // slot qui contient une direction (repere local de la planete)
        public int SlotAt(Vector3 direction)
        {
            int vertex = NearestVertex(direction);
            if (vertex < 0)
                return -1;
            foreach (int s in VertexSlots[vertex])
                if (Contains(s, direction))
                    return s;
            // triangle tres etire: le sommet le plus proche n'est pas un de ses coins
            foreach (int n in VertexNeighbors[vertex])
                foreach (int s in VertexSlots[n])
                    if (Contains(s, direction))
                        return s;
            return NearestSlot(direction);
        }

        public bool Contains(int slot, Vector3 direction)
        {
            var t = Triangles[slot];
            float w = slotWinding[slot];
            for (int e = 0; e < 3; e++)
            {
                var p = Points[t[e]];
                var q = Points[t[(e + 1) % 3]];
                if (w * Vector3.Dot(Vector3.Cross(p, q), direction) < -1e-7f * Radius * Radius)
                    return false;
            }
            return true;
        }

        public int NearestSlot(Vector3 direction)
        {
            int best = -1;
            float bestDot = float.NegativeInfinity;
            for (int s = 0; s < Slots.Count; s++)
            {
                float d = Vector3.Dot(Slots[s].center.normalized, direction);
                if (d > bestDot)
                {
                    bestDot = d;
                    best = s;
                }
            }
            return best;
        }

        // sommets a moins de `angle` radians d'une direction
        public List<int> VerticesWithin(Vector3 direction, float angle)
        {
            float minDot = Mathf.Cos(Mathf.Min(angle, Mathf.PI));
            var result = new List<int>();
            direction = direction.normalized;
            for (int v = 0; v < Points.Count; v++)
                if (Vector3.Dot(Points[v], direction) >= minDot * Points[v].magnitude)
                    result.Add(v);
            return result;
        }

        // slots dont le centre est a moins de `angle` radians d'une direction
        public List<int> SlotsWithin(Vector3 direction, float angle)
        {
            float minDot = Mathf.Cos(Mathf.Min(angle, Mathf.PI));
            var result = new List<int>();
            direction = direction.normalized;
            for (int s = 0; s < Slots.Count; s++)
                if (Vector3.Dot(Slots[s].center.normalized, direction) >= minDot)
                    result.Add(s);
            return result;
        }

        #endregion

        #region Voisinages

        // slots qui touchent au moins un des sommets
        public HashSet<int> SlotsAroundVertices(IEnumerable<int> vertices)
        {
            var result = new HashSet<int>();
            foreach (int v in vertices)
                result.UnionWith(VertexSlots[v]);
            return result;
        }

        // slots qui partagent un sommet avec un des slots (couronne de largeur 1)
        public HashSet<int> TouchingSlots(IEnumerable<int> slots)
        {
            var result = new HashSet<int>();
            foreach (int s in slots)
                foreach (int v in Triangles[s])
                    result.UnionWith(VertexSlots[v]);
            return result;
        }

        // les slots et `rings` couronnes de voisins autour (voisins par sommet)
        public HashSet<int> ExpandSlots(IEnumerable<int> slots, int rings)
        {
            var result = new HashSet<int>(slots);
            var frontier = new List<int>(result);
            for (int r = 0; r < rings && frontier.Count > 0; r++)
            {
                var next = new List<int>();
                foreach (int s in frontier)
                    foreach (int v in Triangles[s])
                        foreach (int n in VertexSlots[v])
                            if (result.Add(n))
                                next.Add(n);
                frontier = next;
            }
            return result;
        }

        #endregion
    }

    // hash stable d'une session a l'autre (System.HashCode est tire au hasard a chaque lancement): signatures serialisees
    public struct StableHash
    {
        private uint value;

        public static StableHash Create() => new StableHash { value = 2166136261u };

        public StableHash Add(int v)
        {
            unchecked
            {
                for (int i = 0; i < 4; i++)
                {
                    value ^= (uint)(v >> (i * 8)) & 0xff;
                    value *= 16777619u;
                }
            }
            return this;
        }

        public StableHash Add(float v) => Add(System.BitConverter.ToInt32(System.BitConverter.GetBytes(v), 0));
        public StableHash Add(bool v) => Add(v ? 1 : 0);

        public StableHash Add(string s)
        {
            if (s == null)
                return Add(-1);
            foreach (char c in s)
                Add(c);
            return Add(s.Length);
        }

        public int Value => (int)value == 0 ? 1 : (int)value;
    }
}
