using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WFCContent.WFC;

namespace WFCContent.World
{
    // forme booleenne posee sur une planete (enfant de la GoldbergPolyhedron): creuse un trou ou monte un plateau dans le relief.
    // les sommets de la grille dans la forme prennent sa hauteur (0 = trou: mer, vide, gouffre), le reste suit: le relief ne
    // change que d'un etage par arete, donc un trou au milieu d'un plateau recoit ses anneaux de pentes (escaliers d'un puits
    // a degres, falaises du desert...). la taille et la position viennent du transform: deplacer / tourner / mettre a
    // l'echelle avec les gizmos de la Scene, la planete se recalcule quand on lache la forme (WFCShapeLivePreview).
    // les formes sont appliquees dans l'ordre de la hierarchie (la derniere gagne), la peinture a la main passe devant
    [ExecuteAlways, DisallowMultipleComponent]
    [AddComponentMenu("WFC/Planet Shape (boolean)")]
    public class WFCPlanetShape : MonoBehaviour
    {
        public enum Kind
        {
            Sphere = 0,
            Box = 1,
            Cylinder = 2  // axe Y local (tourne vers le centre de la planete pour un puits)
        }

        public enum Operation
        {
            Subtract = 0,  // creuser: le sol descend a `height` (s'il etait plus haut)
            Union = 1,     // ajouter: le sol monte a `height` (s'il etait plus bas)
            Set = 2        // imposer: le sol est a `height`
        }

        // poids de la cible d'un sommet dans une forme (1 = relief procedural, la peinture a la main est a 4)
        public const float TargetWeight = 4f;

        [SerializeField, Tooltip("Toutes les formes tiennent dans un cube de 1 (comme le Cube de Unity): l'echelle du transform donne la taille")]
        public Kind kind = Kind.Sphere;
        [SerializeField, Tooltip("Creuser: le sol descend a la hauteur. Ajouter: il monte a la hauteur. Imposer: il est a la hauteur")]
        public Operation operation = Operation.Subtract;
        [SerializeField, Min(0), Tooltip("Hauteur dans la forme: 0 = trou (mer, vide), 1 = terre, 2..N = etages, N + 1 = montagne")]
        public int height = 0;
        [SerializeField, Tooltip("Dur: le WFC doit respecter la hauteur (le trou est exactement la). Sinon simple cible, plus forte que le relief")]
        public bool hard = true;

        // formes actives (toutes planetes): la live preview de l'editeur les surveille sans parcourir les stamps
        private static readonly HashSet<WFCPlanetShape> active = new();
        public static IReadOnlyCollection<WFCPlanetShape> Active => active;

        [System.NonSerialized] private GoldbergPolyhedron planet;
        [System.NonSerialized] private bool planetSearched;

        // planete a laquelle la forme s'applique: la GoldbergPolyhedron parente
        public GoldbergPolyhedron Planet
        {
            get
            {
                if (!planetSearched || (planet == null && !ReferenceEquals(planet, null)))
                {
                    planet = GetComponentInParent<GoldbergPolyhedron>(true);
                    planetSearched = true;
                }
                return planet;
            }
        }

        public Color Color => operation switch
        {
            Operation.Subtract => new Color(1f, 0.35f, 0.2f),
            Operation.Union => new Color(0.35f, 0.9f, 0.35f),
            _ => new Color(0.35f, 0.65f, 1f)
        };

        private void OnEnable() => active.Add(this);

        private void OnDisable() => active.Remove(this);

        private void OnTransformParentChanged() => planetSearched = false;

        // axe Y de la forme le long de la verticale de la planete (cylindre = puits droit), posee sur la surface
        [ContextMenu("Aligner sur la surface")]
        private void AlignToSurface()
        {
            var p = Planet;
            if (p == null)
                return;
#if UNITY_EDITOR
            UnityEditor.Undo.RecordObject(transform, "Aligner sur la surface");
#endif
            var center = p.transform.position;
            var up = (transform.position - center).normalized;
            if (up.sqrMagnitude < 1e-6f)
                up = p.transform.up;
            float surface = p.Grid.Radius * p.transform.lossyScale.x;
            transform.SetPositionAndRotation(center + up * surface, Quaternion.FromToRotation(Vector3.up, up));
        }

        // point (repere monde) dans la forme
        public bool Contains(Vector3 world)
        {
            var p = transform.InverseTransformPoint(world);
            return kind switch
            {
                Kind.Box => Mathf.Abs(p.x) <= 0.5f && Mathf.Abs(p.y) <= 0.5f && Mathf.Abs(p.z) <= 0.5f,
                Kind.Cylinder => p.x * p.x + p.z * p.z <= 0.25f && Mathf.Abs(p.y) <= 0.5f,
                _ => p.sqrMagnitude <= 0.25f
            };
        }

        // hauteur d'un sommet apres la forme. current: hauteur avant (relief, formes precedentes), null = inconnue
        public int Apply(int? current)
        {
            if (current == null)
                return height;
            return operation switch
            {
                Operation.Subtract => Mathf.Min(current.Value, height),
                Operation.Union => Mathf.Max(current.Value, height),
                _ => height
            };
        }

        // formes actives d'une planete, dans l'ordre de la hierarchie (la derniere gagne)
        public static WFCPlanetShape[] Of(GoldbergPolyhedron planet) =>
            planet == null ? new WFCPlanetShape[0] : planet.GetComponentsInChildren<WFCPlanetShape>(false).Where(s => s.enabled).ToArray();

        // hauteur imposee par les formes a chaque sommet touche: (hauteur, dure). targets: carte de relief (peut etre null)
        public static Dictionary<int, (int height, bool hard)> VertexHeights(GoldbergPolyhedron planet, PlanetGrid grid, int floors,
            float[] targets, IReadOnlyList<WFCPlanetShape> shapes = null)
        {
            var result = new Dictionary<int, (int, bool)>();
            shapes ??= Of(planet);
            if (shapes.Count == 0)
                return result;
            var toWorld = planet.transform.localToWorldMatrix;
            for (int v = 0; v < grid.VertexCount; v++)
            {
                var world = toWorld.MultiplyPoint3x4(grid.Points[v]);
                int? current = targets != null && v < targets.Length && !float.IsNaN(targets[v]) ? Mathf.RoundToInt(targets[v]) : null;
                bool touched = false, hard = false;
                foreach (var shape in shapes)
                {
                    if (!shape.Contains(world))
                        continue;
                    current = shape.Apply(current);
                    hard = shape.hard;
                    touched = true;
                }
                if (touched)
                    result[v] = (Mathf.Clamp(current.Value, 0, floors + 1), hard);
            }
            return result;
        }

        // contraintes du WFC (appele par GoldbergPolyhedron.Solve, apres la peinture): les sommets dans les formes prennent leur
        // hauteur (dure ou cible), les autres sont ramenes a un etage au plus de leurs voisins (pentes autour des trous).
        // les sommets peints a la main ne sont pas touches
        public static void Apply(GoldbergPolyhedron planet, PlanetGrid grid, int floors, WFCPlanetPainting painting,
            ref float[] targets, ref WFCConstraints constraints)
        {
            var shapes = Of(planet);
            if (shapes.Length == 0)
                return;
            var heights = VertexHeights(planet, grid, floors, targets, shapes);
            if (painting != null && painting.Matches(grid))
                foreach (var mark in painting.Heights)
                    heights.Remove(mark.vertex);
            if (heights.Count == 0)
                return;

            int vertexCount = grid.VertexCount;
            targets ??= Enumerable.Repeat(float.NaN, vertexCount).ToArray();
            // sommets fixes pendant la mise en pente: les formes et la peinture (qui l'a deja faite pour elle)
            var fixedTargets = heights.ToDictionary(kv => kv.Key, kv => (float)kv.Value.height);
            if (painting != null && painting.Matches(grid))
                foreach (var mark in painting.Heights)
                    if (mark.vertex >= 0 && mark.vertex < vertexCount)
                        fixedTargets[mark.vertex] = Mathf.Clamp(mark.height, 0, floors + 1);
            // planete a niveaux (trous a tous les etages): un trou ne tire pas ses voisins vers le bas, le plateau y tombe d'un
            // bloc. on le pose apres la mise en pente
            var theme = planet.ActiveTheme;
            var holes = theme != null && theme.holesAnyLevel
                ? heights.Where(kv => kv.Value.height == 0).Select(kv => kv.Key).ToList()
                : new List<int>();
            foreach (int v in holes)
                fixedTargets.Remove(v);
            ReliefMap.ApplyPaint(targets, fixedTargets, grid.Triangles);
            foreach (int v in holes)
                targets[v] = 0f;

            constraints ??= new WFCConstraints();
            if (constraints.TargetWeights == null)
            {
                constraints.TargetWeights = new float[vertexCount];
                for (int v = 0; v < vertexCount; v++)
                    constraints.TargetWeights[v] = float.IsNaN(targets[v]) ? 0f : 1f;
            }
            foreach (var (v, (h, hard)) in heights.Select(kv => (kv.Key, kv.Value)))
            {
                constraints.TargetWeights[v] = Mathf.Max(constraints.TargetWeights[v], TargetWeight);
                if (!hard)
                    continue;
                constraints.VertexHeights ??= Enumerable.Repeat(-1, vertexCount).ToArray();
                if (constraints.VertexHeights[v] < 0)
                    constraints.VertexHeights[v] = h;
            }
            // resolus en premier: sinon une cote ou une riviere voisine decide avant la forme
            constraints.Priority ??= new bool[grid.SlotCount];
            foreach (int s in grid.SlotsAroundVertices(heights.Keys))
                constraints.Priority[s] = true;
        }

        public static bool AnyOn(GoldbergPolyhedron planet) => active.Any(s => s != null && s.isActiveAndEnabled && s.Planet == planet);

        // change quand une forme de la planete bouge ou change (live preview de l'editeur, GoldbergPolyhedron.BuildSignature).
        // stable d'une session a l'autre (StableHash, ordre de la hierarchie, pas d'instance id): la signature est serialisee
        public static int Signature(GoldbergPolyhedron planet)
        {
            var hash = StableHash.Create();
            if (planet == null)
                return hash.Value;
            var toPlanet = planet.transform.worldToLocalMatrix;
            var shapes = active.Where(s => s != null && s.isActiveAndEnabled && s.Planet == planet)
                .Select(s => (shape: s, order: HierarchyOrder(s.transform, planet.transform)))
                .OrderBy(s => s.order, System.StringComparer.Ordinal);
            foreach (var (shape, order) in shapes)
            {
                hash = hash.Add(order).Add((int)shape.kind).Add((int)shape.operation).Add(shape.height).Add(shape.hard);
                var m = toPlanet * shape.transform.localToWorldMatrix;
                for (int i = 0; i < 16; i++)
                    hash = hash.Add(Mathf.Round(m[i] * 1e4f));
            }
            return hash.Value;
        }

        // place dans la hierarchie sous la planete ("3.12": 13e enfant du 4e enfant), triable comme du texte
        private static string HierarchyOrder(Transform t, Transform root)
        {
            var parts = new List<string>();
            for (; t != null && t != root; t = t.parent)
                parts.Add(t.GetSiblingIndex().ToString("D5"));
            parts.Reverse();
            return string.Join(".", parts);
        }

        #region Gizmos

        private static Mesh cylinderMesh;

        private void OnDrawGizmos() => DrawShape(false);

        private void OnDrawGizmosSelected()
        {
            DrawShape(true);
            // sommets de la grille dans la forme: ce que la forme va changer, visible pendant le deplacement
            var p = Planet;
            if (p == null)
                return;
            var grid = p.Grid;
            var toWorld = p.transform.localToWorldMatrix;
            float size = grid.MeanEdge * 0.12f * p.transform.lossyScale.x;
            Gizmos.matrix = Matrix4x4.identity;
            Gizmos.color = Color;
            int drawn = 0;
            for (int v = 0; v < grid.VertexCount && drawn < 4000; v++)
            {
                var world = toWorld.MultiplyPoint3x4(grid.Points[v]);
                if (!Contains(world))
                    continue;
                Gizmos.DrawCube(world, Vector3.one * size);
                drawn++;
            }
        }

        private void DrawShape(bool selected)
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            var color = Color;
            color.a = selected ? 1f : 0.7f;
            Gizmos.color = color;
            switch (kind)
            {
                case Kind.Box:
                    Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
                    break;
                case Kind.Cylinder:
                    if (cylinderMesh == null)
                        cylinderMesh = Resources.GetBuiltinResource<Mesh>("Cylinder.fbx");
                    // le cylindre de Unity fait 2 de haut
                    Gizmos.DrawWireMesh(cylinderMesh, Vector3.zero, Quaternion.identity, new Vector3(1f, 0.5f, 1f));
                    break;
                default:
                    Gizmos.DrawWireSphere(Vector3.zero, 0.5f);
                    break;
            }
            if (!selected)
                return;
            color.a = 0.12f;
            Gizmos.color = color;
            switch (kind)
            {
                case Kind.Box:
                    Gizmos.DrawCube(Vector3.zero, Vector3.one);
                    break;
                case Kind.Cylinder:
                    Gizmos.DrawMesh(cylinderMesh, Vector3.zero, Quaternion.identity, new Vector3(1f, 0.5f, 1f));
                    break;
                default:
                    Gizmos.DrawSphere(Vector3.zero, 0.5f);
                    break;
            }
        }

        #endregion
    }
}
