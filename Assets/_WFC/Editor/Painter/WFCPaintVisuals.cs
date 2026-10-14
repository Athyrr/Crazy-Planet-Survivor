using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using WFCContent.World;

namespace Editor
{
    // affichage des calques peints dans la vue Scene: un seul mesh (couleurs par sommet), reconstruit quand la peinture change
    public static class WFCPaintVisuals
    {
        public static readonly Color StampColor = new(0.20f, 0.85f, 0.95f);
        public static readonly Color LockColor = new(0.55f, 0.62f, 1.00f);
        public static readonly Color ConflictColor = new(1.00f, 0.20f, 0.15f);

        private static Material material;
        private static Mesh mesh;
        private static WFCPlanetPainting builtFor;
        private static int builtVersion = -1;
        private static int builtSettings;

        public static void Invalidate() => builtVersion = -1;

        public static void Draw(GoldbergPolyhedron planet, WFCPlanetPainting painting, WFCPaintSettings settings)
        {
            if (Event.current.type != EventType.Repaint || planet == null || painting == null)
                return;
            var grid = planet.Grid;
            if (!painting.Matches(grid))
                return;

            int settingsHash = Hash(settings, planet);
            if (mesh == null || builtFor != painting || builtVersion != painting.Version || builtSettings != settingsHash)
            {
                Rebuild(planet, painting, settings);
                builtFor = painting;
                builtVersion = painting.Version;
                builtSettings = settingsHash;
            }
            if (mesh.vertexCount == 0)
                return;

            var mat = Material();
            mat.SetInt("_ZTest", (int)(settings.xray ? CompareFunction.Always : CompareFunction.LessEqual));
            mat.SetPass(0);
            Graphics.DrawMeshNow(mesh, planet.transform.localToWorldMatrix);
        }

        private static int Hash(WFCPaintSettings s, GoldbergPolyhedron planet) => StableHash.Create()
            .Add(s.showRelief).Add(s.showStamps).Add(s.showZones).Add(s.showLocks).Add(s.showConflicts).Add(s.opacity)
            .Add(planet.LastConflictCount).Add(planet.GetHashCode())
            .Value;

        private static Material Material()
        {
            if (material != null)
                return material;
            material = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave };
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            material.SetInt("_Cull", (int)CullMode.Back);
            material.SetInt("_ZWrite", 0);
            material.SetFloat("_ZBias", -2f);
            return material;
        }

        private static void Rebuild(GoldbergPolyhedron planet, WFCPlanetPainting painting, WFCPaintSettings s)
        {
            if (mesh == null)
                mesh = new Mesh { name = "WFC Paint Overlay", hideFlags = HideFlags.HideAndDontSave, indexFormat = IndexFormat.UInt32 };
            mesh.Clear();

            var grid = planet.Grid;
            var solved = painting.SolvedVertexHeights(grid);
            float floorStep = planet.LayerHeight * planet.FloorHeight;
            float lift = planet.LayerHeight * 0.08f;
            float opacity = s.opacity;
            var vertices = new List<Vector3>();
            var colors = new List<Color>();
            var indices = new List<int>();

            // sommet a la surface du terrain (hauteur resolue), legerement au-dessus
            Vector3 Surface(int v) => grid.Points[v] * (1f + floorStep * solved[v] + lift);

            void Triangle(Vector3 a, Vector3 b, Vector3 c, Color color)
            {
                // face avant vers l'exterieur de la planete (Cull Back cache l'autre hemisphere)
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), a + b + c) < 0f)
                    (b, c) = (c, b);
                int i = vertices.Count;
                vertices.Add(a);
                vertices.Add(b);
                vertices.Add(c);
                colors.Add(color);
                colors.Add(color);
                colors.Add(color);
                indices.Add(i);
                indices.Add(i + 1);
                indices.Add(i + 2);
            }

            // inset: 0 = tout le slot, 1 = son centre
            void Slot(int slot, float inset, Color color)
            {
                var t = grid.Triangles[slot];
                Vector3 a = Surface(t[0]), b = Surface(t[1]), c = Surface(t[2]);
                Vector3 center = (a + b + c) / 3f;
                Triangle(Vector3.Lerp(a, center, inset), Vector3.Lerp(b, center, inset), Vector3.Lerp(c, center, inset), color);
            }

            void Hexagon(int vertex, float radius, float hole, Color color)
            {
                Vector3 center = Surface(vertex) + grid.Points[vertex].normalized * lift * grid.Radius * 0.5f;
                Vector3 normal = grid.Points[vertex].normalized;
                Vector3 tangent = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
                Vector3 bitangent = Vector3.Cross(normal, tangent);
                for (int k = 0; k < 6; k++)
                {
                    float a0 = k * Mathf.PI / 3f, a1 = (k + 1) * Mathf.PI / 3f;
                    Vector3 d0 = tangent * Mathf.Cos(a0) + bitangent * Mathf.Sin(a0);
                    Vector3 d1 = tangent * Mathf.Cos(a1) + bitangent * Mathf.Sin(a1);
                    if (hole <= 0f)
                    {
                        Triangle(center, center + d0 * radius, center + d1 * radius, color);
                        continue;
                    }
                    Vector3 o0 = center + d0 * radius, o1 = center + d1 * radius;
                    Vector3 i0 = center + d0 * radius * hole, i1 = center + d1 * radius * hole;
                    Triangle(i0, o0, o1, color);
                    Triangle(i0, o1, i1, color);
                }
            }

            if (s.showZones)
                foreach (var mark in painting.Zones)
                    if (mark.zone != null && mark.slot >= 0 && mark.slot < grid.SlotCount)
                        Slot(mark.slot, 0.04f, WithAlpha(mark.zone.color, 0.55f * opacity));

            if (s.showStamps)
                foreach (var mark in painting.Stamps)
                    if (mark.slot >= 0 && mark.slot < grid.SlotCount)
                        Slot(mark.slot, 0.32f, WithAlpha(StampColor, 0.6f * opacity));

            if (s.showLocks)
                foreach (var mark in painting.Locks)
                    if (mark.slot >= 0 && mark.slot < grid.SlotCount)
                        Slot(mark.slot, 0.6f, WithAlpha(LockColor, 0.75f * opacity));

            if (s.showConflicts)
                foreach (int slot in planet.ConflictSlots)
                    if (slot >= 0 && slot < grid.SlotCount)
                        Slot(slot, 0.45f, WithAlpha(ConflictColor, 0.85f));

            if (s.showRelief)
            {
                float radius = grid.MeanEdge * 0.2f;
                int floors = planet.Floors;
                foreach (var mark in painting.Heights)
                {
                    if (mark.vertex < 0 || mark.vertex >= grid.VertexCount)
                        continue;
                    var color = WithAlpha(WFCPaintGUI.HeightColor(mark.height, floors), Mathf.Lerp(0.5f, 1f, opacity));
                    // imposee = pastille pleine, guide = anneau
                    Hexagon(mark.vertex, radius, mark.force ? 0f : 0.55f, color);
                }
            }

            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetTriangles(indices, 0);
            mesh.RecalculateBounds();
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }
    }
}
