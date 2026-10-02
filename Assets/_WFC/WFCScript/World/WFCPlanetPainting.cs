using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WFCContent.WFC;

namespace WFCContent.World
{
    // peinture a la main de la planete (outil WFC Paint de la Scene, Tools/WFC/Planet Painter).
    // 4 calques de marques, lues par GoldbergPolyhedron a chaque resolution du WFC:
    //  - relief (par sommet): hauteur imposee (dur) ou cible du relief (souple)
    //  - stamps (par slot): pieces autorisees, avec rotation eventuelle
    //  - zones (par slot): regles d'une WFCPaintZone (favoriser / eviter / interdire des pieces)
    //  - gel (par slot): la piece posee ne bouge plus
    // garde aussi la derniere solution (une piece par slot): annuler / refaire et les retouches locales partent d'elle.
    // les marques gardent leur direction: si la grille change (densite, graine de grille...), elles suivent
    [DisallowMultipleComponent, RequireComponent(typeof(GoldbergPolyhedron))]
    public class WFCPlanetPainting : MonoBehaviour, ISerializationCallbackReceiver
    {
        // poids de la cible d'un sommet peint (1 = relief procedural)
        public const float PaintedTargetWeight = 4f;
        // force du relief quand la planete n'a pas de carte de relief (seules les marques guident le WFC)
        public const float PaintedReliefStrength = 6f;

        public enum Layer { Relief, Stamps, Zones, Locks }

        [Serializable]
        public class HeightMark
        {
            public int vertex;
            public Vector3 direction;
            public int height;
            public bool force;
        }

        [Serializable]
        public class StampMark
        {
            public int slot;
            public Vector3 direction;
            public List<WFCElementData> elements = new();
            public int rotation = -1;
        }

        [Serializable]
        public class ZoneMark
        {
            public int slot;
            public Vector3 direction;
            public WFCPaintZone zone;
        }

        [Serializable]
        public class LockMark
        {
            public int slot;
            public Vector3 direction;
            public WFCSlotKey key;
        }

        [SerializeField, Tooltip("En jeu, Generate reconstruit la planete telle qu'elle a ete peinte (sans relancer le WFC), " +
                                 "tant que la grille et le theme n'ont pas change")]
        private bool reuseInPlayMode = true;

        [SerializeField, HideInInspector] private int gridSignature;
        [SerializeField, HideInInspector] private List<HeightMark> heights = new();
        [SerializeField, HideInInspector] private List<StampMark> stamps = new();
        [SerializeField, HideInInspector] private List<ZoneMark> zones = new();
        [SerializeField, HideInInspector] private List<LockMark> locks = new();
        [SerializeField, HideInInspector] private List<WFCSlotKey> solution = new();
        [SerializeField, HideInInspector] private WFCPlanetTheme solutionTheme;
        [SerializeField, HideInInspector] private int salt;

        [NonSerialized] private Dictionary<int, HeightMark> heightIndex;
        [NonSerialized] private Dictionary<int, StampMark> stampIndex;
        [NonSerialized] private Dictionary<int, ZoneMark> zoneIndex;
        [NonSerialized] private Dictionary<int, LockMark> lockIndex;
        [NonSerialized] private int[] solvedHeights;
        [NonSerialized] private int solvedHeightsVersion = -1;

        // change a chaque modification (marques, solution, annuler): l'affichage de la peinture se reconstruit
        public int Version { get; private set; } = 1;

        public bool ReuseInPlayMode => reuseInPlayMode;
        public IReadOnlyList<HeightMark> Heights => heights;
        public IReadOnlyList<StampMark> Stamps => stamps;
        public IReadOnlyList<ZoneMark> Zones => zones;
        public IReadOnlyList<LockMark> Locks => locks;
        public IReadOnlyList<WFCSlotKey> Solution => solution;
        public bool IsEmpty => heights.Count + stamps.Count + zones.Count + locks.Count == 0;

        public int Count(Layer layer) => layer switch
        {
            Layer.Relief => heights.Count,
            Layer.Stamps => stamps.Count,
            Layer.Zones => zones.Count,
            _ => locks.Count
        };

        #region Grille et solution

        // marques ramenees sur la grille actuelle (sommet / slot le plus proche de leur direction) si elle a change
        public void MatchGrid(PlanetGrid grid)
        {
            if (gridSignature == grid.Signature)
                return;
            if (gridSignature != 0)
            {
                Remap(heights, m => m.direction, (m, i) => m.vertex = i, grid.NearestVertex, m => m.vertex);
                Remap(stamps, m => m.direction, (m, i) => m.slot = i, grid.SlotAt, m => m.slot);
                Remap(zones, m => m.direction, (m, i) => m.slot = i, grid.SlotAt, m => m.slot);
                // une piece gelee n'a de sens que sur son slot d'origine: les hauteurs autour ont change
                locks.Clear();
            }
            solution.Clear();
            gridSignature = grid.Signature;
            ResetIndices();
            Changed();
        }

        private static void Remap<T>(List<T> marks, Func<T, Vector3> direction, Action<T, int> setIndex, Func<Vector3, int> find, Func<T, int> index)
        {
            foreach (var mark in marks)
                setIndex(mark, find(direction(mark)));
            // deux marques sur le meme element: la derniere gagne
            var seen = new HashSet<int>();
            for (int i = marks.Count - 1; i >= 0; i--)
                if (index(marks[i]) < 0 || !seen.Add(index(marks[i])))
                    marks.RemoveAt(i);
        }

        public bool HasSolution(int slotCount) => gridSignature != 0 && solution.Count == slotCount;

        // les index des marques correspondent a cette grille
        public bool Matches(PlanetGrid grid) => gridSignature == grid.Signature;

        // solution posee par un Generate complet (sans annulation)
        public void StoreSolution(IReadOnlyList<WFCSlotKey> keys, WFCPlanetTheme theme)
        {
            solution = keys.ToList();
            solutionTheme = theme;
            Changed();
        }

        // solution d'une retouche locale (l'outil de peinture enregistre l'annulation avant)
        public void SetSolution(IReadOnlyList<WFCSlotKey> keys)
        {
            solution = keys.ToList();
            Changed();
        }

        // solution a reconstruire telle quelle, si elle correspond encore a la grille et au theme
        public WFCSlotKey[] StoredSolution(PlanetGrid grid, WFCPlanetTheme theme) =>
            gridSignature == grid.Signature && solution.Count == grid.SlotCount && solutionTheme == theme ? solution.ToArray() : null;

        // nouveau tirage pour chaque retouche locale
        public int NextSalt() => ++salt;

        // hauteur de chaque sommet dans la solution actuelle (0 sans solution)
        public int[] SolvedVertexHeights(PlanetGrid grid)
        {
            if (solvedHeights != null && solvedHeightsVersion == Version && solvedHeights.Length == grid.VertexCount)
                return solvedHeights;
            solvedHeights = new int[grid.VertexCount];
            var known = new bool[grid.VertexCount];
            if (solution.Count == grid.SlotCount)
            {
                for (int s = 0; s < solution.Count; s++)
                {
                    if (solution[s].element == null)
                        continue;
                    var cornerHeights = solution[s].CornerHeights();
                    var vertices = grid.Triangles[s];
                    for (int k = 0; k < 3; k++)
                    {
                        if (known[vertices[k]])
                            continue;
                        known[vertices[k]] = true;
                        solvedHeights[vertices[k]] = cornerHeights[k];
                    }
                }
            }
            solvedHeightsVersion = Version;
            return solvedHeights;
        }

        // hauteur peinte, sinon celle de la solution
        public int EffectiveHeight(int vertex, int[] solved) =>
            TryGetHeight(vertex, out var mark) ? mark.height : solved != null && vertex < solved.Length ? solved[vertex] : 0;

        #endregion

        #region Marques

        public bool TryGetHeight(int vertex, out HeightMark mark) => HeightIndex.TryGetValue(vertex, out mark);
        public StampMark GetStamp(int slot) => StampIndex.TryGetValue(slot, out var mark) ? mark : null;
        public ZoneMark GetZone(int slot) => ZoneIndex.TryGetValue(slot, out var mark) ? mark : null;
        public LockMark GetLock(int slot) => LockIndex.TryGetValue(slot, out var mark) ? mark : null;

        public bool SetHeight(int vertex, Vector3 direction, int height, bool force)
        {
            if (TryGetHeight(vertex, out var mark))
            {
                if (mark.height == height && mark.force == force)
                    return false;
                mark.height = height;
                mark.force = force;
            }
            else
            {
                mark = new HeightMark { vertex = vertex, direction = direction.normalized, height = height, force = force };
                heights.Add(mark);
                HeightIndex[vertex] = mark;
            }
            Changed();
            return true;
        }

        public bool ClearHeight(int vertex) => Remove(heights, HeightIndex, vertex);

        public bool SetStamp(int slot, Vector3 direction, IReadOnlyCollection<WFCElementData> elements, int rotation)
        {
            var mark = GetStamp(slot);
            if (mark != null)
            {
                if (mark.rotation == rotation && mark.elements.Count == elements.Count && elements.All(mark.elements.Contains))
                    return false;
            }
            else
            {
                mark = new StampMark { slot = slot, direction = direction.normalized };
                stamps.Add(mark);
                StampIndex[slot] = mark;
            }
            mark.elements = elements.ToList();
            mark.rotation = rotation;
            Changed();
            return true;
        }

        public bool ClearStamp(int slot) => Remove(stamps, StampIndex, slot);

        public bool SetZone(int slot, Vector3 direction, WFCPaintZone zone)
        {
            var mark = GetZone(slot);
            if (mark != null)
            {
                if (mark.zone == zone)
                    return false;
                mark.zone = zone;
            }
            else
            {
                mark = new ZoneMark { slot = slot, direction = direction.normalized, zone = zone };
                zones.Add(mark);
                ZoneIndex[slot] = mark;
            }
            Changed();
            return true;
        }

        public bool ClearZone(int slot) => Remove(zones, ZoneIndex, slot);

        public bool SetLock(int slot, Vector3 direction, WFCSlotKey key)
        {
            var mark = GetLock(slot);
            if (mark != null)
            {
                if (mark.key.Equals(key))
                    return false;
                mark.key = key;
            }
            else
            {
                mark = new LockMark { slot = slot, direction = direction.normalized, key = key };
                locks.Add(mark);
                LockIndex[slot] = mark;
            }
            Changed();
            return true;
        }

        public bool ClearLock(int slot) => Remove(locks, LockIndex, slot);

        // efface un calque, renvoie les slots a re-resoudre
        public HashSet<int> ClearLayer(Layer layer, PlanetGrid grid)
        {
            HashSet<int> dirty = layer switch
            {
                Layer.Relief => grid.SlotsAroundVertices(heights.Select(m => m.vertex).Where(v => v >= 0 && v < grid.VertexCount)),
                Layer.Stamps => new HashSet<int>(stamps.Select(m => m.slot)),
                Layer.Zones => new HashSet<int>(zones.Select(m => m.slot)),
                _ => new HashSet<int>(locks.Select(m => m.slot))
            };
            switch (layer)
            {
                case Layer.Relief: heights.Clear(); break;
                case Layer.Stamps: stamps.Clear(); break;
                case Layer.Zones: zones.Clear(); break;
                default: locks.Clear(); break;
            }
            dirty.RemoveWhere(s => s < 0 || s >= grid.SlotCount);
            ResetIndices();
            Changed();
            return dirty;
        }

        public HashSet<int> SlotsWithZone(WFCPaintZone zone) => new(zones.Where(m => m.zone == zone).Select(m => m.slot));

        private bool Remove<T>(List<T> marks, Dictionary<int, T> index, int key) where T : class
        {
            if (!index.TryGetValue(key, out var mark))
                return false;
            marks.Remove(mark);
            index.Remove(key);
            Changed();
            return true;
        }

        private Dictionary<int, HeightMark> HeightIndex => heightIndex ??= Index(heights, m => m.vertex);
        private Dictionary<int, StampMark> StampIndex => stampIndex ??= Index(stamps, m => m.slot);
        private Dictionary<int, ZoneMark> ZoneIndex => zoneIndex ??= Index(zones, m => m.slot);
        private Dictionary<int, LockMark> LockIndex => lockIndex ??= Index(locks, m => m.slot);

        private static Dictionary<int, T> Index<T>(List<T> marks, Func<T, int> key)
        {
            var index = new Dictionary<int, T>();
            foreach (var mark in marks)
                if (mark != null)
                    index[key(mark)] = mark;
            return index;
        }

        private void Changed()
        {
            Version++;
#if UNITY_EDITOR
            if (!Application.isPlaying)
                UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

        // annuler / refaire, rechargement: les index sont reconstruits
        public void OnBeforeSerialize() { }

        public void OnAfterDeserialize()
        {
            ResetIndices();
            Version++;
        }

        private void ResetIndices()
        {
            heightIndex = null;
            stampIndex = null;
            zoneIndex = null;
            lockIndex = null;
        }

        #endregion

        #region Contraintes

        // contraintes pour le WFC. targets: carte de relief (null s'il n'y en a pas), les sommets peints y sont ajoutes.
        // holes: theme a trous a tous les etages (un trou touche n'importe quel etage par une falaise): un trou peint ne tire pas
        // ses voisins vers le bas (pas d'entonnoir d'etages autour)
        public WFCConstraints BuildConstraints(PlanetGrid grid, int floors, ref float[] targets, bool holes = false)
        {
            if (IsEmpty)
                return null;
            var constraints = new WFCConstraints();
            int slotCount = grid.SlotCount;
            bool ValidSlot(int s) => s >= 0 && s < slotCount;

            if (heights.Count > 0)
            {
                var painted = new Dictionary<int, float>();
                foreach (var mark in heights)
                {
                    if (mark.vertex < 0 || mark.vertex >= grid.VertexCount)
                        continue;
                    int height = Mathf.Clamp(mark.height, 0, floors + 1);
                    painted[mark.vertex] = height;
                    if (!mark.force)
                        continue;
                    if (constraints.VertexHeights == null)
                        constraints.VertexHeights = Enumerable.Repeat(-1, grid.VertexCount).ToArray();
                    constraints.VertexHeights[mark.vertex] = height;
                }

                // sans carte de relief, seuls les sommets peints ont une cible
                targets ??= Enumerable.Repeat(float.NaN, grid.VertexCount).ToArray();
                if (holes)
                {
                    ReliefMap.ApplyPaint(targets, painted.Where(kv => kv.Value > 0f).ToDictionary(kv => kv.Key, kv => kv.Value), grid.Triangles);
                    foreach (var kv in painted)
                        if (kv.Value <= 0f)
                            targets[kv.Key] = 0f;
                }
                else
                {
                    ReliefMap.ApplyPaint(targets, painted, grid.Triangles);
                }
                constraints.TargetWeights = new float[grid.VertexCount];
                for (int v = 0; v < grid.VertexCount; v++)
                    constraints.TargetWeights[v] = painted.ContainsKey(v) ? PaintedTargetWeight : float.IsNaN(targets[v]) ? 0f : 1f;

                // le relief peint est resolu en premier: sinon une riviere ou une cote voisine decide avant lui
                constraints.Priority ??= new bool[slotCount];
                foreach (int s in grid.SlotsAroundVertices(painted.Keys))
                    constraints.Priority[s] = true;
            }

            foreach (var mark in stamps)
            {
                if (!ValidSlot(mark.slot) || mark.elements == null)
                    continue;
                var allowed = new HashSet<WFCElementData>(mark.elements.Where(e => e != null));
                if (allowed.Count == 0)
                    continue;
                constraints.Allowed ??= new HashSet<WFCElementData>[slotCount];
                constraints.Allowed[mark.slot] = allowed;
                if (mark.rotation < 0)
                    continue;
                constraints.AllowedRotation ??= Enumerable.Repeat(-1, slotCount).ToArray();
                constraints.AllowedRotation[mark.slot] = mark.rotation % 3;
            }

            var factors = new Dictionary<WFCPaintZone, Func<WFCElementData, float>>();
            foreach (var mark in zones)
            {
                if (!ValidSlot(mark.slot) || mark.zone == null)
                    continue;
                if (!factors.TryGetValue(mark.zone, out var factor))
                    factors[mark.zone] = factor = mark.zone.FactorFor;
                constraints.Weights ??= new Func<WFCElementData, float>[slotCount];
                constraints.Weights[mark.slot] = factor;
                constraints.Priority ??= new bool[slotCount];
                constraints.Priority[mark.slot] = true;
            }

            foreach (var mark in locks)
            {
                if (!ValidSlot(mark.slot) || mark.key.element == null)
                    continue;
                constraints.Fixed ??= new WFCSlotKey[slotCount];
                constraints.Fixed[mark.slot] = mark.key;
            }

            return constraints;
        }

        #endregion
    }
}
