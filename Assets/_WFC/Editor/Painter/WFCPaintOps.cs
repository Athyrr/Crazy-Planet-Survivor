using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEditor;
using UnityEngine;
using WFCContent.WFC;
using WFCContent.World;
using Debug = UnityEngine.Debug;

namespace Editor
{
    // un trait de pinceau (souris enfoncee -> relachee): une seule etape d'annulation
    public class PaintStroke
    {
        public GoldbergPolyhedron planet;
        public WFCPlanetPainting painting;
        public PaintTool tool;
        public bool erase;
        public int undoGroup;
        // hauteurs au debut du trait: monter / descendre / lisser ne s'appliquent qu'une fois par sommet
        public int[] startHeights;
        public int flattenHeight;
        public PaintHit last;
        // slots a re-resoudre (pas encore resolus) et tous les slots du trait
        public readonly HashSet<int> pending = new();
        public readonly HashSet<int> touched = new();
    }

    public struct SolveReport
    {
        public int region;
        public int rebuilt;
        public int conflicts;
        public int relaxed;
        public long milliseconds;
        public bool full;

        public override string ToString() => full
            ? $"Planète régénérée · {conflicts} conflit(s) · {milliseconds} ms"
            : $"{region} slots résolus, {rebuilt} reconstruits · {conflicts} conflit(s)" +
              (relaxed > 0 ? $" · {relaxed} contrainte(s) impossible(s)" : "") + $" · {milliseconds} ms";
    }

    // operations du pinceau, sans entree souris (testables). toutes passent par l'annulation de Unity
    public static class WFCPaintOps
    {
        public const string UndoName = "WFC Paint";

        public static SolveReport LastReport;
        public static bool HasReport;

        #region Preparation

        // composant de peinture + planete construite avec les reglages actuels (sinon les index ne correspondent a rien)
        public static WFCPlanetPainting EnsureReady(GoldbergPolyhedron planet)
        {
            var painting = planet.GetComponent<WFCPlanetPainting>();
            if (painting == null)
                painting = Undo.AddComponent<WFCPlanetPainting>(planet.gameObject);
            if (!planet.IsBuilt || !painting.Matches(planet.Grid) || !painting.HasSolution(planet.Grid.SlotCount))
                Regenerate(planet, painting, "WFC Paint: génération");
            return painting;
        }

        public static bool IsReady(GoldbergPolyhedron planet, WFCPlanetPainting painting) =>
            planet != null && painting != null && planet.IsBuilt && painting.Matches(planet.Grid) && painting.HasSolution(planet.Grid.SlotCount);

        // Generate complet (garde la peinture et le gel). annulable: la solution precedente revient avec ses stamps
        public static void Regenerate(GoldbergPolyhedron planet, WFCPlanetPainting painting, string undoName = "WFC Paint: régénérer")
        {
            var watch = Stopwatch.StartNew();
            if (painting != null)
                Undo.RecordObject(painting, undoName);
            planet.Generate();
            Report(new SolveReport { full = true, conflicts = planet.LastConflictCount, milliseconds = watch.ElapsedMilliseconds });
            AfterChange();
        }

        private static GoldbergPolyhedron instancesPlanet;
        private static int instancesVersion = -1;
        private static Dictionary<int, WFCStampInstance> instances;

        // stamps par slot, en cache tant que la planete ne change pas
        public static Dictionary<int, WFCStampInstance> Instances(GoldbergPolyhedron planet, WFCPlanetPainting painting)
        {
            int version = painting != null ? painting.Version : 0;
            if (instances == null || instancesPlanet != planet || instancesVersion != version)
            {
                instances = planet.StampInstances();
                instancesPlanet = planet;
                instancesVersion = version;
            }
            return instances;
        }

        public static void AfterChange()
        {
            instances = null;
            WFCPaintRaycast.ClearCache();
            WFCPaintVisuals.Invalidate();
            SceneView.RepaintAll();
        }

        private static void Report(SolveReport report)
        {
            LastReport = report;
            HasReport = true;
        }

        #endregion

        #region Trait

        public static PaintStroke BeginStroke(GoldbergPolyhedron planet, WFCPlanetPainting painting, WFCPaintSettings settings, PaintHit hit, bool erase)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(UndoName);
            var grid = planet.Grid;
            var solved = painting.SolvedVertexHeights(grid);
            var stroke = new PaintStroke
            {
                planet = planet,
                painting = painting,
                tool = settings.tool,
                erase = erase || settings.tool == PaintTool.Erase,
                undoGroup = Undo.GetCurrentGroup(),
                startHeights = Enumerable.Range(0, grid.VertexCount).Select(v => painting.EffectiveHeight(v, solved)).ToArray(),
                last = hit
            };
            stroke.flattenHeight = stroke.startHeights[hit.vertex];

            if (settings.shape == BrushShape.Fill)
                Fill(stroke, settings, hit);
            else
                Dab(stroke, settings, hit);
            return stroke;
        }

        // pose le pinceau tous les ~tiers de rayon entre le dernier point et celui-ci (pas de trous quand la souris va vite)
        public static void DragTo(PaintStroke stroke, WFCPaintSettings settings, PaintHit hit)
        {
            if (settings.shape == BrushShape.Fill)
                return;
            var grid = stroke.planet.Grid;
            float spacing = settings.shape == BrushShape.Cell
                ? grid.MeanEdge / grid.Radius * 0.4f
                : Mathf.Max(BrushAngle(grid, settings) * 0.35f, grid.MeanEdge / grid.Radius * 0.3f);
            float angle = Vector3.Angle(stroke.last.direction, hit.direction) * Mathf.Deg2Rad;
            int steps = Mathf.Clamp(Mathf.CeilToInt(angle / spacing), 1, 64);
            for (int i = 1; i <= steps; i++)
            {
                var direction = Vector3.Slerp(stroke.last.direction, hit.direction, i / (float)steps).normalized;
                var step = hit;
                step.direction = direction;
                step.slot = i == steps ? hit.slot : grid.SlotAt(direction);
                step.vertex = i == steps ? hit.vertex : grid.NearestVertex(direction);
                Dab(stroke, settings, step);
            }
            stroke.last = hit;
        }

        // re-resout ce qui a ete peint depuis la derniere resolution
        public static void ResolvePending(PaintStroke stroke, WFCPaintSettings settings)
        {
            if (stroke.pending.Count == 0)
                return;
            var dirty = new HashSet<int>(stroke.pending);
            stroke.pending.Clear();
            Resolve(stroke.planet, stroke.painting, dirty, settings.margin);
        }

        public static void EndStroke(PaintStroke stroke, WFCPaintSettings settings)
        {
            ResolvePending(stroke, settings);
            Undo.CollapseUndoOperations(stroke.undoGroup);
        }

        // rayon du pinceau en radians sur la sphere
        public static float BrushAngle(PlanetGrid grid, WFCPaintSettings settings) => settings.size * grid.MeanEdge / grid.Radius;

        public static List<int> BrushVertices(PlanetGrid grid, WFCPaintSettings settings, PaintHit hit)
        {
            if (settings.shape != BrushShape.Circle)
                return new List<int> { hit.vertex };
            var vertices = grid.VerticesWithin(hit.direction, BrushAngle(grid, settings));
            if (vertices.Count == 0)
                vertices.Add(hit.vertex);
            return vertices;
        }

        public static List<int> BrushSlots(PlanetGrid grid, WFCPaintSettings settings, PaintHit hit)
        {
            if (settings.shape != BrushShape.Circle)
                return new List<int> { hit.slot };
            var slots = grid.SlotsWithin(hit.direction, BrushAngle(grid, settings));
            if (!slots.Contains(hit.slot))
                slots.Add(hit.slot);
            return slots;
        }

        public static void Dab(PaintStroke stroke, WFCPaintSettings settings, PaintHit hit)
        {
            var grid = stroke.planet.Grid;
            bool usesVertices = stroke.tool == PaintTool.Relief || (stroke.tool == PaintTool.Erase && settings.eraseRelief);
            bool usesSlots = stroke.tool != PaintTool.Relief;
            var vertices = new List<int>();
            if (usesVertices)
                vertices = stroke.tool == PaintTool.Relief && !stroke.erase
                    ? ReliefVertices(stroke.planet, settings, hit)
                    : BrushVertices(grid, settings, hit);
            Apply(stroke, settings, vertices, usesSlots ? BrushSlots(grid, settings, hit) : new List<int>());
        }

        // sommets peints par l'outil Relief: hauteur choisie seulement la ou un stamp peut la poser (un pic de montagne va au
        // centre d'une cellule, pas sur un coin). forme Cellule: le sommet possible le plus proche de la souris
        public static List<int> ReliefVertices(GoldbergPolyhedron planet, WFCPaintSettings settings, PaintHit hit)
        {
            var grid = planet.Grid;
            if (settings.reliefOp != ReliefOp.Paint)
                return BrushVertices(grid, settings, hit);
            var possible = PossibleHeights(planet);
            if (settings.shape == BrushShape.Cell)
            {
                var near = grid.VertexNeighbors[hit.vertex].Append(hit.vertex)
                    .Where(v => possible[v].Contains(settings.height))
                    .OrderByDescending(v => Vector3.Dot(grid.Points[v].normalized, hit.direction))
                    .Take(1)
                    .ToList();
                return near.Count > 0 ? near : new List<int> { hit.vertex };
            }
            return BrushVertices(grid, settings, hit).Where(v => possible[v].Contains(settings.height)).ToList();
        }

        private static GoldbergPolyhedron possiblePlanet;
        private static int possibleKey;
        private static HashSet<int>[] possibleHeights;

        // hauteurs que les stamps du theme peuvent donner a chaque sommet: intersection, sur les slots du sommet, des hauteurs
        // que les pieces (rotations et etages compris) mettent a la place qu'il y occupe
        public static HashSet<int>[] PossibleHeights(GoldbergPolyhedron planet)
        {
            var grid = planet.Grid;
            var elements = WFCPaintGUI.PlanetElements(planet);
            int floors = planet.Floors;
            var hash = StableHash.Create().Add(grid.Signature).Add(floors);
            foreach (var element in elements)
                hash = hash.Add(element.GetHashCode()).Add(element.heightApex).Add(element.heightCornerA).Add(element.heightCornerB)
                    .Add(element.allowRotation).Add(element.peak).Add(element.stackable);
            if (possibleHeights != null && possiblePlanet == planet && possibleKey == hash.Value)
                return possibleHeights;

            var roles = new[] { new HashSet<int>(), new HashSet<int>(), new HashSet<int>() };
            foreach (var element in elements)
            {
                var source = element.CornerHeights;
                int rotations = element.allowRotation ? 3 : 1;
                foreach (int level in element.Levels(floors))
                    for (int r = 0; r < rotations; r++)
                        for (int i = 0; i < 3; i++)
                            roles[(i + r) % 3].Add(source[i] + level);
            }

            possibleHeights = new HashSet<int>[grid.VertexCount];
            for (int v = 0; v < grid.VertexCount; v++)
            {
                HashSet<int> heights = null;
                foreach (int s in grid.VertexSlots[v])
                {
                    var role = roles[System.Array.IndexOf(grid.Triangles[s], v)];
                    if (heights == null)
                        heights = new HashSet<int>(role);
                    else
                        heights.IntersectWith(role);
                }
                possibleHeights[v] = heights ?? new HashSet<int>();
            }
            possiblePlanet = planet;
            possibleKey = hash.Value;
            return possibleHeights;
        }

        // pot de peinture: tous les sommets de meme hauteur / slots de meme piece (ou meme zone) relies a celui sous la souris
        public static void Fill(PaintStroke stroke, WFCPaintSettings settings, PaintHit hit)
        {
            const int limit = 20000;
            var grid = stroke.planet.Grid;
            var painting = stroke.painting;
            var vertices = new List<int>();
            var slots = new List<int>();

            if (stroke.tool == PaintTool.Relief || (stroke.tool == PaintTool.Erase && settings.eraseRelief))
            {
                int height = stroke.startHeights[hit.vertex];
                vertices = Flood(hit.vertex, v => grid.VertexNeighbors[v], v => stroke.startHeights[v] == height, limit);
            }
            if (stroke.tool != PaintTool.Relief)
            {
                var solution = painting.Solution;
                bool hasSolution = solution.Count == grid.SlotCount;
                var zone = painting.GetZone(hit.slot)?.zone;
                var element = hasSolution ? solution[hit.slot].element : null;
                System.Func<int, bool> same = stroke.tool == PaintTool.Zone
                    ? s => painting.GetZone(s)?.zone == zone
                    : s => !hasSolution || solution[s].element == element;
                slots = Flood(hit.slot, s => grid.SlotNeighbors[s], same, limit);
            }
            Apply(stroke, settings, vertices, slots);
        }

        private static List<int> Flood(int start, System.Func<int, IEnumerable<int>> neighbors, System.Func<int, bool> same, int limit)
        {
            var result = new List<int> { start };
            var seen = new HashSet<int> { start };
            for (int i = 0; i < result.Count && result.Count < limit; i++)
                foreach (int n in neighbors(result[i]))
                    if (seen.Add(n) && same(n))
                        result.Add(n);
            return result;
        }

        private static void Apply(PaintStroke stroke, WFCPaintSettings settings, List<int> vertices, List<int> slots)
        {
            var planet = stroke.planet;
            var painting = stroke.painting;
            var grid = planet.Grid;
            Undo.RecordObject(painting, UndoName);

            void Dirty(IEnumerable<int> dirtySlots)
            {
                foreach (int s in dirtySlots)
                {
                    stroke.pending.Add(s);
                    stroke.touched.Add(s);
                }
            }

            switch (stroke.tool)
            {
                case PaintTool.Relief when stroke.erase:
                case PaintTool.Erase:
                    if (stroke.tool == PaintTool.Relief || settings.eraseRelief)
                        foreach (int v in vertices)
                            if (painting.ClearHeight(v))
                                Dirty(grid.VertexSlots[v]);
                    if (stroke.tool == PaintTool.Erase)
                        foreach (int s in slots)
                        {
                            bool changed = false;
                            changed |= settings.eraseStamps && painting.ClearStamp(s);
                            changed |= settings.eraseZones && painting.ClearZone(s);
                            // degeler garde la piece: rien a re-resoudre
                            if (settings.eraseLocks)
                                painting.ClearLock(s);
                            if (changed)
                                Dirty(new[] { s });
                        }
                    break;

                case PaintTool.Relief:
                    PaintRelief(stroke, settings, vertices, Dirty);
                    break;

                case PaintTool.Stamp:
                    PaintStamps(stroke, settings, slots, Dirty);
                    break;

                case PaintTool.Zone:
                    foreach (int s in slots)
                    {
                        bool changed = stroke.erase || settings.zone == null
                            ? painting.ClearZone(s)
                            : painting.SetZone(s, grid.Slots[s].center, settings.zone);
                        if (changed)
                            Dirty(new[] { s });
                    }
                    break;

                case PaintTool.Lock:
                    foreach (int s in slots)
                    {
                        if (stroke.erase)
                            painting.ClearLock(s);
                        else if (painting.Solution.Count == grid.SlotCount && painting.Solution[s].element != null)
                            painting.SetLock(s, grid.Slots[s].center, painting.Solution[s]);
                    }
                    break;

                case PaintTool.Reroll:
                    // chaque passage du pinceau retire au sort (les slots deja resolus pendant ce trait aussi)
                    Dirty(slots);
                    break;
            }
        }

        private static void PaintRelief(PaintStroke stroke, WFCPaintSettings settings, List<int> vertices, System.Action<IEnumerable<int>> dirty)
        {
            var planet = stroke.planet;
            var painting = stroke.painting;
            var grid = planet.Grid;
            int top = planet.Floors + 1;
            var possible = PossibleHeights(planet);
            foreach (int v in vertices)
            {
                int start = stroke.startHeights[v];
                int height = settings.reliefOp switch
                {
                    ReliefOp.Raise => start + 1,
                    ReliefOp.Lower => start - 1,
                    ReliefOp.Flatten => stroke.flattenHeight,
                    ReliefOp.Smooth => Mathf.RoundToInt((float)grid.VertexNeighbors[v].Append(v).Average(n => stroke.startHeights[n])),
                    _ => settings.height
                };
                height = Mathf.Clamp(height, 0, top);
                // aucun stamp ne pose cette hauteur ici (pic sur un coin de cellule): on n'ajoute pas une contrainte impossible
                if (!possible[v].Contains(height))
                    continue;
                // lisser n'ajoute pas de marque la ou rien ne change
                if (settings.reliefOp == ReliefOp.Smooth && height == start && !painting.TryGetHeight(v, out _))
                    continue;
                if (painting.SetHeight(v, grid.Points[v], height, settings.force))
                    dirty(grid.VertexSlots[v]);
            }
        }

        private static void PaintStamps(PaintStroke stroke, WFCPaintSettings settings, List<int> slots, System.Action<IEnumerable<int>> dirty)
        {
            var planet = stroke.planet;
            var painting = stroke.painting;
            var grid = planet.Grid;
            if (stroke.erase)
            {
                foreach (int s in slots)
                    if (painting.ClearStamp(s))
                        dirty(new[] { s });
                return;
            }

            var selection = settings.stamps.Where(e => e != null).Distinct().ToList();
            if (selection.Count == 0)
                return;
            int rotation = settings.shape == BrushShape.Cell && selection.Count == 1 ? settings.stampRotation : -1;
            var solved = painting.SolvedVertexHeights(grid);
            int floors = planet.Floors;
            foreach (int s in slots)
            {
                var allowed = selection;
                if (settings.stampFitRelief)
                {
                    var corners = grid.Triangles[s].Select(v => painting.EffectiveHeight(v, solved)).ToArray();
                    allowed = selection.Where(e => Fits(e, corners, floors, rotation)).ToList();
                    if (allowed.Count == 0)
                        continue;
                }
                if (painting.SetStamp(s, grid.Slots[s].center, allowed, rotation))
                    dirty(new[] { s });
            }
        }

        // la piece peut etre posee sur ces hauteurs de coins (une de ses rotations, un de ses etages)
        public static bool Fits(WFCElementData element, int[] corners, int floors, int rotation = -1)
        {
            var source = element.CornerHeights;
            var rotations = rotation >= 0 ? new[] { rotation % 3 } : element.allowRotation ? new[] { 0, 1, 2 } : new[] { 0 };
            foreach (int level in element.Levels(floors))
                foreach (int r in rotations)
                {
                    bool ok = true;
                    for (int i = 0; i < 3 && ok; i++)
                        ok = source[i] + level == corners[(i + r) % 3];
                    if (ok)
                        return true;
                }
            return false;
        }

        #endregion

        #region Pipette, resolution

        // Ctrl + clic: reprend la valeur sous la souris
        public static string Pick(GoldbergPolyhedron planet, WFCPlanetPainting painting, WFCPaintSettings settings, PaintHit hit)
        {
            var grid = planet.Grid;
            switch (settings.tool)
            {
                case PaintTool.Relief:
                    settings.height = painting.EffectiveHeight(hit.vertex, painting.SolvedVertexHeights(grid));
                    settings.reliefOp = ReliefOp.Paint;
                    settings.Changed();
                    return "Hauteur : " + WFCPaintGUI.HeightLabel(settings.height, planet.Floors, WFCPaintGUI.HasHoles(planet));
                case PaintTool.Stamp:
                    if (painting.Solution.Count != grid.SlotCount || painting.Solution[hit.slot].element == null)
                        return null;
                    var key = painting.Solution[hit.slot];
                    settings.stamps = new List<WFCElementData> { key.element };
                    settings.stampRotation = settings.shape == BrushShape.Cell ? key.rotation : -1;
                    settings.Changed();
                    return "Stamp : " + WFCPaintGUI.PrettyName(key.element);
                case PaintTool.Zone:
                    var zone = painting.GetZone(hit.slot)?.zone;
                    if (zone == null)
                        return null;
                    settings.zone = zone;
                    settings.Changed();
                    return "Zone : " + zone.name;
                default:
                    return null;
            }
        }

        // re-resolution locale autour des slots, puis seuls les slots qui changent sont reconstruits
        public static SolveReport Resolve(GoldbergPolyhedron planet, WFCPlanetPainting painting, ICollection<int> dirty, int margin)
        {
            if (dirty.Count == 0)
                return default;
            if (!IsReady(planet, painting))
            {
                Regenerate(planet, painting);
                return LastReport;
            }

            var watch = Stopwatch.StartNew();
            Undo.RecordObject(painting, UndoName);
            SolveReport report;
            try
            {
                var result = planet.SolveRegion(painting.Solution, dirty, margin, painting.NextSalt());
                painting.SetSolution(result.Solution);
                var rebuilt = planet.ApplySolution(result.Solution);
                report = new SolveReport
                {
                    region = result.Region.Count, rebuilt = rebuilt.Count, conflicts = result.Conflicts, relaxed = result.Relaxed,
                    milliseconds = watch.ElapsedMilliseconds
                };
            }
            catch (System.Exception e)
            {
                Debug.LogException(e, planet);
                report = default;
            }
            Report(report);
            AfterChange();
            return report;
        }

        // re-resout des slots en dehors d'un trait (zone modifiee, calque efface...)
        public static SolveReport ResolveSlots(GoldbergPolyhedron planet, WFCPlanetPainting painting, ICollection<int> slots, string undoName)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(undoName);
            int group = Undo.GetCurrentGroup();
            var report = Resolve(planet, painting, slots, WFCPaintSettings.instance.margin);
            Undo.CollapseUndoOperations(group);
            return report;
        }

        public static void ClearLayer(GoldbergPolyhedron planet, WFCPlanetPainting painting, WFCPlanetPainting.Layer layer)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("WFC Paint: effacer un calque");
            int group = Undo.GetCurrentGroup();
            Undo.RecordObject(painting, UndoName);
            var dirty = painting.ClearLayer(layer, planet.Grid);
            // degeler garde les pieces: rien a re-resoudre
            if (layer != WFCPlanetPainting.Layer.Locks)
                Resolve(planet, painting, dirty, WFCPaintSettings.instance.margin);
            Undo.CollapseUndoOperations(group);
            AfterChange();
        }

        public static void LockAll(GoldbergPolyhedron planet, WFCPlanetPainting painting)
        {
            var grid = planet.Grid;
            if (painting.Solution.Count != grid.SlotCount)
                return;
            Undo.RecordObject(painting, "WFC Paint: tout geler");
            for (int s = 0; s < grid.SlotCount; s++)
                if (painting.Solution[s].element != null)
                    painting.SetLock(s, grid.Slots[s].center, painting.Solution[s]);
            AfterChange();
        }

        #endregion
    }

    // annuler / refaire: la solution enregistree revient, les stamps suivent (seuls les slots qui changent)
    [InitializeOnLoad]
    public static class WFCPaintUndo
    {
        static WFCPaintUndo()
        {
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        private static void OnUndoRedo()
        {
            foreach (var painting in Object.FindObjectsByType<WFCPlanetPainting>())
            {
                var planet = painting.GetComponent<GoldbergPolyhedron>();
                if (planet == null || !planet.isActiveAndEnabled || !WFCPaintOps.IsReady(planet, painting))
                    continue;
                try
                {
                    planet.ApplySolution(painting.Solution);
                }
                catch (System.Exception e)
                {
                    Debug.LogException(e, planet);
                }
            }
            WFCPaintOps.AfterChange();
        }
    }
}
