using System.Collections.Generic;
using System.Linq;
using EasyButtons;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using WFCContent.WFC;
using Random = UnityEngine.Random;

namespace WFCContent.World
{
    // un triangle de cellule = un emplacement pour une piece
    public struct WFCSlot
    {
        public int o, a, b;
        public int[] vertexIds; // id globaux de [o, a, b], partages entre slots voisins
        public Vector3 center;
    }

    public struct DEBUG_cellsData
    {
        public Vector3 center;
        public Vector3 rotation;

        public float extra;

        public DEBUG_cellsData(Vector3 center, Vector3 rotation, float extra = -1f)
        {
            this.center = center;
            this.rotation = rotation;
            this.extra = extra;
        }
    }
    
    // j'ai suivis ce truc https://mathcurve.com/polyedres/geode/geode.shtml (c'etait pas simple mais normalement c'est stable)
    // pour les matrix chelou et tout: https://doc.babylonjs.com/guidedLearning/workshop/Geodesic_Code/ (merci google)
    [RequireComponent(typeof(MeshRenderer), typeof(MeshFilter), typeof(SphereCollider))]
    public class GoldbergPolyhedron : MonoBehaviour
    {
        [SerializeField, Range(1, 10)] private int density = 1;
        [SerializeField, Range(1, 50)] private float radius = 1;
        [SerializeField] private bool enableLattice = true;
        [SerializeField] private bool enableLatticeDebug = false;
        
        [SerializeField] private GameObject defaultStamp;
        [SerializeField] private int seed = 0;
        [SerializeField, Tooltip("Stamps et look de la planete (terre, lave...). Vide = theme par defaut de la WFCDatabase, sinon les stamps sans theme")]
        private WFCPlanetTheme theme;
        [SerializeField, Tooltip("Couleur du niveau 0 quand il n'y a aucun theme")]
        private Color groundColor = new Color(0.70f, 0.80f, 0.78f);
        [SerializeField, Tooltip("Debug: une couleur aleatoire par cellule")]
        private bool randomCellColors = false;
        [SerializeField, Range(0.01f, 0.3f), Tooltip("Hauteur de la cage d'un stamp, en fraction du rayon (0.1 = 10% du rayon): echelle verticale " +
                                                     "du relief. Un etage de terrain = floorHeight du theme (0.35) x cette hauteur")]
        private float layerHeight = 0.1f;
        [SerializeField, Tooltip("Les stamps epousent la courbure de la planete (sinon chaque stamp est une facette plane)")]
        private bool followCurvature = true;
        [SerializeField, Range(1, 8), Tooltip("Subdivision de chaque triangle de la mer (niveau 0) pour suivre la courbure")]
        private int seaSubdivisions = 4;

        [Header("Grille (facon Townscaper)")]
        [SerializeField, Range(0f, 1f), Tooltip("Aretes retournees au hasard: sommets a 5, 6 ou 7 triangles au lieu d'un motif regulier")]
        private float irregularity = 0.35f;
        [SerializeField, Range(0, 100), Tooltip("Iterations de relaxation: chaque triangle tend vers un triangle equilateral")]
        private int relaxIterations = 40;
        [SerializeField, Range(0f, 1f)] private float relaxStrength = 0.5f;
        [SerializeField, Range(0f, 1f), Tooltip("0 = chaque triangle garde sa taille, 1 = tous visent la taille moyenne (stamps a la meme echelle)")]
        private float sizeEqualization = 0.75f;
        [SerializeField, Tooltip("Graine de la grille, separee de la graine du WFC")]
        private int gridSeed = 0;

        [Header("Relief (continents, facon Planetary Annihilation)")]
        [SerializeField, Range(0f, 10f), Tooltip("0 = WFC libre (terres tres morcelees). Plus fort = le WFC suit la carte de relief")]
        private float reliefStrength = 6f;
        [SerializeField, Range(0.3f, 4f), Tooltip("Frequence du relief: plus petit = continents plus grands")]
        private float reliefScale = 1.6f;
        [SerializeField, Range(0f, 1f), Tooltip("Part des sommets sous la mer")]
        private float seaFraction = 0.55f;
        [SerializeField, Range(0f, 0.5f), Tooltip("Part des sommets en montagne")]
        private float mountainFraction = 0.08f;
        [SerializeField, Range(1, MaxFloors), Tooltip(FloorsTooltip)]
        private int floors = 1;
        [SerializeField, Range(0.1f, 0.9f), Tooltip(TerraceRatioTooltip)]
        private float terraceRatio = 0.5f;

        public const int MaxFloors = 5;
        public const string FloorsTooltip = "Hauteur de la planete en etages de terrain au-dessus de la mer. 1 = terre + montagnes (comme avant), " +
                                            "2-3 = plateaux relies par des pentes douces (stamps RAMP_*), les montagnes vont sur le dernier etage";
        public const string TerraceRatioTooltip = "Part de chaque etage qui monte a l'etage du dessus: petit = petits plateaux, grand = grands plateaux";
        // = FLOOR_STEP / H du generateur Blender, pour les planetes sans theme (sinon WFCPlanetTheme.floorHeight)
        public const float DefaultFloorHeight = 0.35f;

        // enfant des prefabs de stamp qui porte le decor et l'eau (pas de collision, pas de lissage des jointures)
        public const string DecorChildName = "Decor";
        private const string GeneratedMeshName = "WFC Planet";
        private const string InstanceSuffix = " (Instance)";
        // meshes crees par cette instance (copies des stamps, mesh de la planete): les seuls qu'on detruit. une planete dupliquee
        // pointe sur les meshes de l'originale, elle les oublie sans les detruire (apres un rechargement ils partent au prochain nettoyage)
        [System.NonSerialized] private readonly HashSet<Mesh> ownedMeshes = new();
        
        public int LastSlotCount { get; private set; }
        public int LastConflictCount { get; private set; }

        public WFCPlanetTheme Theme { get => theme; set => theme = value; }
        // theme de la planete, sinon celui par defaut de la WFCDatabase. null = ancien comportement (stamps sans theme, groundColor)
        public WFCPlanetTheme ActiveTheme => theme != null ? theme : WFCDatabase.Instance.DefaultTheme;

        public int Seed { get => seed; set => seed = value; }
        public float Radius => radius;
        public float LayerHeight => layerHeight;
        // hauteur d'un etage en fraction de la cage, etages de terrain (relief du theme s'il le remplace)
        public float FloorHeight => FloorHeightOf(ActiveTheme);
        public int Floors => Mathf.Max(1, ReliefSettings(ActiveTheme).floors);
        // rayon du niveau 0 (mer, ou fond des gouffres) dans le repere de la planete
        public float SeaRadius => Grid.Radius * SeaScale(ActiveTheme);
        public IReadOnlyList<int> ConflictSlots => conflictSlots;
        // les stamps poses correspondent aux reglages actuels (grille, cage, theme): la peinture peut ne retoucher que quelques slots
        public bool IsBuilt => builtSignature != 0 && builtSignature == BuildSignature(ActiveTheme);

        // grille de la planete, reconstruite (a l'identique) apres un rechargement ou un changement de ses reglages
        public PlanetGrid Grid
        {
            get
            {
                if (grid == null || grid.Signature != GridSignature())
                    grid = BuildGrid();
                return grid;
            }
        }

        [System.NonSerialized] private PlanetGrid grid;
        private List<Vector3> debugVirtualPoint = new List<Vector3>();
        private List<DEBUG_cellsData> _debugCellsDatas = new List<DEBUG_cellsData>();

        private SphereCollider sc;
        [SerializeField, HideInInspector] private List<GameObject> stampsCached = new();
        [SerializeField, HideInInspector] private List<Vector3> debugConflicts = new();
        [SerializeField, HideInInspector] private List<int> conflictSlots = new();
        // material de la planete avant qu'un theme le remplace (null = pas remplace)
        [SerializeField, HideInInspector] private Material planetMaterial;
        // reglages avec lesquels les stamps actuels ont ete poses (cf. IsBuilt)
        [SerializeField, HideInInspector] private int builtSignature;
        
        #region Core

#if UNITY_EDITOR
        [System.NonSerialized] private bool themeValidated;
        [System.NonSerialized] private WFCPlanetTheme validatedTheme;
#endif

        private void OnValidate()
        {
            if (sc == null)
                sc = GetComponent<SphereCollider>();
            sc.radius = radius - 0.15f;

#if UNITY_EDITOR
            // theme change dans l'inspector: la live preview regenere avec les nouveaux stamps (pas au chargement)
            bool themeChanged = themeValidated && validatedTheme != theme;
            themeValidated = true;
            validatedTheme = theme;
            if (themeChanged)
                WFCElementData.NotifyRulesChanged();
#endif
        }

        #endregion

        [Button]
        public void Generate()
        {
            ClearCache();
            // la grille de debug de la FFD pese des dizaines de Mo sur une planete: seulement quand on l'affiche
            FreeFormDeformer.CollectDebug = enableLatticeDebug;
            grid = BuildGrid();
            BuildPlanet(grid);
        }

        // changement de planete en jeu (CrazyPlanet: planet.Generate(themeDeLaPlanete))
        public void Generate(WFCPlanetTheme newTheme)
        {
            theme = newTheme;
            Generate();
        }

        private void ClearCache()
        {
            foreach (var stamp in stampsCached)
                DestroyStamp(stamp);
            stampsCached.Clear();
            // stamps que la liste a perdus (annulation d'une modif de la planete: la liste revient a des stamps deja detruits)
            var lost = new List<GameObject>();
            foreach (Transform child in transform)
                if (child.GetComponent<WFCStampInstance>() != null)
                    lost.Add(child.gameObject);
            foreach (var stamp in lost)
                DestroyStamp(stamp);
            debugConflicts.Clear();
            conflictSlots.Clear();

            _debugCellsDatas.Clear();
            debugVirtualPoint.Clear();
            FreeFormDeformer.ClearDebug();
        }

        private void DestroyStamp(GameObject stamp)
        {
            if (stamp == null)
                return;
            // copies des meshes faites par InstantiateStamp, sinon elles fuient a chaque Generate (changement de planete en jeu)
            foreach (var meshFilter in stamp.GetComponentsInChildren<MeshFilter>(true))
                if (meshFilter.sharedMesh != null && ownedMeshes.Remove(meshFilter.sharedMesh))
                    DestroyGenerated(meshFilter.sharedMesh);
            DestroyImmediate(stamp);
        }
        
        const float PHI = 1.61803398875f;

        public static readonly int[][] Faces =
        {
            new[] { 0, 2, 1 }, new[] { 0, 3, 2 }, new[] { 0, 4, 3 }, new[] { 0, 5, 4 }, 
            new[] { 0, 1, 5 }, new[] { 7, 6, 1 }, new[] { 8, 7, 2 }, new[] { 9, 8, 3 }, 
            new[] { 10, 9, 4 }, new[] { 6, 10, 5 }, new[] { 2, 7, 1 }, new[] { 3, 8, 2 },
            new[] { 4, 9, 3 }, new[] { 5, 10, 4 }, new[] { 1, 6, 5 }, new[] { 11, 6, 7 }, 
            new[] { 11, 7, 8 }, new[] { 11, 8, 9 }, new[] { 11, 9, 10 }, new[] { 11, 10, 6 }
        };

        public static readonly Vector3[] Vertex =
        {
            new Vector3(0, PHI, -1), new Vector3(-PHI, 1, 0), new Vector3(-1, 0, -PHI),
            new Vector3(1, 0, -PHI), new Vector3(PHI, 1, 0), new Vector3(0, PHI, 1),
            new Vector3(-1, 0, PHI), new Vector3(-PHI, -1, 0), new Vector3(0, -PHI, -1),
            new Vector3(PHI, -1, 0), new Vector3(1, 0, PHI), new Vector3(0, -PHI, 1)
        };
        
        // de base c'est x et y. mais si on combine les 2 cela peut ce traduire en density
        // todo use m & n 
        private List<Vector2Int> ComputePlanarData(int d) 
        {
            var vertices = new List<Vector2Int>();
            for (int j = 0; j <= d; j++)
                for (int i = 0; i <= d - j; i++)
                    vertices.Add(new Vector2Int(i - j, i + 2 * j));

            return vertices;
        }
        
        private List<int[]> TriangulatePattern(int d)
        {
            var triangles = new List<int[]>();

            var rowStart = new int[d + 1];
            int idx = 0;
            for (int j = 0; j <= d; j++)
            {
                rowStart[j] = idx;
                idx += (d - j + 1);
            }

            for (int j = 0; j < d; j++)
            {
                int countThisRow = d - j + 1;
                int countNextRow = d - j;

                for (int i = 0; i < countThisRow - 1; i++)
                {
                    int p0 = rowStart[j] + i;         // (i, j)
                    int p1 = rowStart[j] + i + 1;      // (i+1, j)
                    int p2 = rowStart[j + 1] + i;       // (i, j+1)

                    triangles.Add(new[] { p0, p2, p1 }); // triangle "montant"

                    if (i < countNextRow - 1)
                    {
                        int p3 = rowStart[j + 1] + i + 1; // (i+1, j+1)
                        triangles.Add(new[] { p1, p2, p3 }); // triangle "descendant"
                    }
                }
            }

            return triangles;
        }
        // icosaedre subdivise, sommets sur la sphere (non soudes: chaque face a les siens)
        private List<Vector3> TranslatePlanarToWorld(List<Vector2Int> planarData, List<int[]> triangulateData, int d, out List<int> meshTriangles)
        {
            List<Vector3> meshVertices = new List<Vector3>();
            meshTriangles = new List<int>();

            float lsqd = d * d + d * d + d * d;
            float coau = (d + d) / lsqd;
            float cobu = -d / lsqd;
            float coav = -GPUtils.THRDR3 * (d - d) / lsqd;
            float cobv = GPUtils.THRDR3 * (2 * d + d) / lsqd;
            
            for (int f = 0; f < Faces.Length; f++)
            {
                int[] faceIndices = Faces[f];
                Vector3 b = Vertex[faceIndices[0]];
                Vector3 a = Vertex[faceIndices[1]];
                Vector3 o = Vertex[faceIndices[2]];

                Vector3 oa = a - o;
                Vector3 ob = b - o;

                Vector3 u = oa * coau + ob * cobu;
                Vector3 v = oa * coav + ob * cobv;

                int baseVertexIndex = meshVertices.Count;

                // octo
                for (int i = 0; i < planarData.Count; i++)
                {
                    Vector3 cart = planarData[i].toCartesianOrigin(Vector2Int.zero);
                    Vector3 pos3D = o + u * cart.x + v * cart.y;

                    pos3D = pos3D.normalized * radius;

                    meshVertices.Add(pos3D);
                }

                // each triangle
                for (int i = 0; i < triangulateData.Count; i++)
                {
                    meshTriangles.Add(baseVertexIndex + triangulateData[i][0]);
                    meshTriangles.Add(baseVertexIndex + triangulateData[i][1]);
                    meshTriangles.Add(baseVertexIndex + triangulateData[i][2]);
                }
            }

            return meshVertices;
        }

        // l'ancien mesh de la planete est detruit s'il vient d'un Generate precedent de cette planete
        private void SetPlanetMesh(Mesh mesh)
        {
            var meshFilter = GetComponent<MeshFilter>();
            var previous = meshFilter.sharedMesh;
            meshFilter.sharedMesh = mesh;
            ownedMeshes.Add(mesh);
            if (previous != null && previous != mesh && ownedMeshes.Remove(previous))
                DestroyGenerated(previous);
        }

        // en jeu les Mesh ne sont pas ramasses avec la planete (changement de planete dans une scene additive)
        private void OnDestroy()
        {
            foreach (var mesh in ownedMeshes)
                if (mesh != null)
                    DestroyGenerated(mesh);
            ownedMeshes.Clear();
        }

        private static void DestroyGenerated(Mesh mesh)
        {
#if UNITY_EDITOR
            if (EditorUtility.IsPersistent(mesh)) // asset: on n'y touche pas
                return;
#endif
            if (Application.isPlaying)
                Destroy(mesh);
            else
                DestroyImmediate(mesh);
        }

        #region Grille

        // icosaedre subdivise -> cellules duales (pentagones / hexagones) -> un slot (centre, coin, coin) par cote de cellule,
        // puis aretes retournees et relaxation. pur (aucun mesh, aucun hasard global): memes reglages = meme grille, a l'octet pres
        private PlanetGrid BuildGrid()
        {
            var planarData = ComputePlanarData(density);
            var triangulateData = TriangulatePattern(density);
            var geodesic = TranslatePlanarToWorld(planarData, triangulateData, density, out var geodesicTriangles);
            var (points, triangles) = BuildDualCells(geodesic, geodesicTriangles);
            return new PlanetGrid(points, triangles) { Signature = GridSignature() };
        }

        private int GridSignature() => StableHash.Create()
            .Add(density).Add(radius).Add(gridSeed).Add(irregularity).Add(relaxIterations).Add(relaxStrength).Add(sizeEqualization)
            .Value;

        // tout ce qui change la pose d'un stamp ou la carte de relief: si rien n'a bouge depuis le dernier Generate, la peinture peut
        // ne re-resoudre que quelques slots (sinon ses retouches suivraient un autre relief que le reste de la planete)
        private int BuildSignature(WFCPlanetTheme active)
        {
            var relief = ReliefSettings(active);
            var hash = StableHash.Create()
                .Add(GridSignature()).Add(layerHeight).Add(followCurvature).Add(enableLattice)
                .Add(active != null ? active.name : "").Add(FloorHeightOf(active)).Add(defaultStamp != null ? defaultStamp.name : "")
                .Add(seed).Add(relief.strength).Add(relief.scale).Add(relief.sea).Add(relief.mountain).Add(relief.floors).Add(relief.terraceRatio);
            // trous a tous les etages: seulement s'ils sont actifs (signature des autres planetes inchangee)
            if (relief.holes > 0f)
                hash = hash.Add(relief.holes);
            // formes booleennes (WFCPlanetShape): une forme deplacee change le relief. rien sans forme: signature inchangee
            if (WFCPlanetShape.AnyOn(this))
                hash = hash.Add(WFCPlanetShape.Signature(this));
            return hash.Value;
        }

        private (List<Vector3> points, List<int[]> triangles) BuildDualCells(List<Vector3> baseVerts, List<int> baseTris)
        {
            // 1) souder les sommets dupliqués (meme position 3D -> meme sommet logique)
            // necessaire car chaque face icosaedre a ses propres copies de sommets
            var weldMap = new Dictionary<Vector3Int, int>();
            var uniquePositions = new List<Vector3>();
            var remap = new int[baseVerts.Count];

            Vector3Int Quantize(Vector3 p) =>
                new Vector3Int(Mathf.RoundToInt(p.x * 1000f), Mathf.RoundToInt(p.y * 1000f), Mathf.RoundToInt(p.z * 1000f));

            for (int i = 0; i < baseVerts.Count; i++)
            {
                var q = Quantize(baseVerts[i]);
                if (!weldMap.TryGetValue(q, out int idx))
                {
                    idx = uniquePositions.Count;
                    uniquePositions.Add(baseVerts[i]);
                    weldMap[q] = idx;
                }
                remap[i] = idx;
            }

            // 2) pour chaque sommet unique, lister les triangles qui le touchent
            int triCount = baseTris.Count / 3;
            var vertexToTris = new Dictionary<int, List<int>>();
            var triCentroids = new Vector3[triCount];

            for (int t = 0; t < triCount; t++)
            {
                int i0 = remap[baseTris[t * 3]];
                int i1 = remap[baseTris[t * 3 + 1]];
                int i2 = remap[baseTris[t * 3 + 2]];

                triCentroids[t] = (uniquePositions[i0] + uniquePositions[i1] + uniquePositions[i2]) / 3f;

                foreach (int v in new[] { i0, i1, i2 })
                {
                    if (!vertexToTris.TryGetValue(v, out var list))
                        vertexToTris[v] = list = new List<int>();
                    if (!list.Contains(t)) list.Add(t);
                }
            }

            // 3) grille des slots: pour chaque polygone (pentagone/hexagone), un triangle (centre, coin, coin) par cote.
            // les sommets sont partages entre cellules: la grille peut ensuite etre retournee/relaxee sans casser les voisinages
            var points = new List<Vector3>();
            var cornerPoint = new Dictionary<int, int>();
            var triangles = new List<int[]>();

            foreach (var kv in vertexToTris)
            {
                var tris = kv.Value;
                if (tris.Count < 3) continue;

                Vector3 center = uniquePositions[kv.Key];
                Vector3 normal = center.normalized;
                Vector3 tangent = Vector3.Cross(normal, Vector3.up);
                if (tangent.sqrMagnitude < 0.001f) tangent = Vector3.Cross(normal, Vector3.right);
                tangent.Normalize();
                Vector3 bitangent = Vector3.Cross(normal, tangent);

                // trier les centroides autour du sommet (ordre angulaire) pour former le contour du polygone
                tris.Sort((a, b) =>
                {
                    Vector3 da = triCentroids[a] - center;
                    Vector3 db = triCentroids[b] - center;
                    float angA = Mathf.Atan2(Vector3.Dot(da, bitangent), Vector3.Dot(da, tangent));
                    float angB = Mathf.Atan2(Vector3.Dot(db, bitangent), Vector3.Dot(db, tangent));
                    return angA.CompareTo(angB);
                });


                // coins = centroides des triangles de base (partages entre 3 cellules), centre = moyenne des coins
                var ring = new int[tris.Count];
                Vector3 cellCenter = Vector3.zero;
                for (int k = 0; k < tris.Count; k++)
                {
                    if (!cornerPoint.TryGetValue(tris[k], out int id))
                    {
                        id = points.Count;
                        points.Add(triCentroids[tris[k]]);
                        cornerPoint[tris[k]] = id;
                    }
                    ring[k] = id;
                    cellCenter += triCentroids[tris[k]];
                }
                int centerId = points.Count;
                points.Add(cellCenter / tris.Count);

                for (int k = 0; k < tris.Count; k++)
                    triangles.Add(new[] { centerId, ring[k], ring[(k + 1) % tris.Count] });
            }

            // 4) grille organique facon Townscaper: aretes retournees au hasard puis relaxation (graine separee du WFC)
            var gridRng = new System.Random(gridSeed);
            if (irregularity > 0f)
                GridRelaxation.FlipEdges(points, triangles, irregularity, gridRng);
            if (relaxIterations > 0)
                GridRelaxation.Relax(points, triangles, relaxIterations, relaxStrength, sizeEqualization);
            // tous les sommets sur la meme sphere (Relax le fait deja): sans relaxation, deux stamps voisins auraient
            // des rayons differents dans la FFD (fente sur l'arete commune)
            float gridRadius = points.Average(p => p.magnitude);
            for (int i = 0; i < points.Count; i++)
                points[i] = points[i].normalized * gridRadius;

            return (points, triangles);
        }

        #endregion

        #region Planete

        // 5) solution du WFC, stamps et mesh du niveau 0 (la mer)
        private void BuildPlanet(PlanetGrid planetGrid)
        {
            var points = planetGrid.Points;
            var triangles = planetGrid.Triangles;
            // theme: stamps, couleur de la mer et relief. null = stamps sans theme de la WFCDatabase et groundColor
            var active = ActiveTheme;
            var cellColors = points
                .Select(p => randomCellColors ? Color.HSVToRGB(Random.value, 0.75f, 0.95f)
                    : active != null ? active.SeaColorAt(p, seed) : groundColor.linear) // vertex color = lineaire
                .ToList();

            // peinture a la main (outil WFC Paint): ses marques suivent la grille, ses contraintes passent au WFC
            var painting = GetComponent<WFCPlanetPainting>();
            if (painting != null)
                painting.MatchGrid(planetGrid);

            // en jeu, une planete peinte est reconstruite telle qu'elle a ete peinte (les retouches locales ne se rejouent pas)
            var keys = painting != null && Application.isPlaying && painting.ReuseInPlayMode ? painting.StoredSolution(planetGrid, active) : null;
            bool solved = keys == null;
            if (solved)
            {
                var solve = Solve(planetGrid, active, painting, seed, null);
                keys = Enumerable.Range(0, planetGrid.SlotCount).Select(solve.Key).ToArray();
                conflictSlots.AddRange(solve.Conflicts);
            }
            LastSlotCount = planetGrid.SlotCount;
            LastConflictCount = conflictSlots.Count;
            RefreshConflictGizmos();

            float floorHeight = FloorHeightOf(active);
            for (int i = 0; i < keys.Length; i++)
                InstantiateSlot(i, keys[i], floorHeight);
            if (enableLattice)
                StitchStampNormals(stampsCached, null);

            // theme: la couleur est evaluee a chaque sommet de la mer (motif net), le debug garde l'interpolation par cellule
            System.Func<Vector3, Color> seaColorAt = null;
            if (active != null && !randomCellColors)
                seaColorAt = p => active.SeaColorAt(p, seed);

            // remplace le mesh triangulé de base. planete a gouffres: le niveau 0 (le vide) descend sous le pied des falaises
            float seaScale = SeaScale(active);
            SetPlanetMesh(followCurvature
                ? BuildSeaMesh(points, triangles, cellColors, seaSubdivisions, seaColorAt, seaScale)
                : BuildFlatMesh(points.Select(v => v * seaScale).ToList(), triangles.SelectMany(t => t).ToList(), cellColors));
            ApplySeaMaterial(active);
            ApplyCoreLight(active);

            builtSignature = BuildSignature(active);
            if (painting != null && solved)
                painting.StoreSolution(keys, active);
        }

        // planete creuse: la sphere du niveau 0 est le noyau (rayon en fraction du rayon), sinon la mer ou le fond des gouffres
        private float SeaScale(WFCPlanetTheme active) =>
            active != null && active.coreRadius > 0f
                ? active.coreRadius
                : Mathf.Max(0.05f, 1f - (active != null ? active.seaDepth : 0f) * layerHeight);

        // un etage plus haut = stamp monte d'une hauteur d'etage dans sa cage FFD (fraction de la cage donnee par les stamps)
        private static float FloorHeightOf(WFCPlanetTheme active) => active != null ? active.floorHeight : DefaultFloorHeight;

        // relief du theme s'il remplace celui de la planete. holes: frequence des trous a tous les etages (0 = trous au niveau 0
        // seulement, comme la mer): propriete des stamps du theme, suivie meme sans overrideRelief
        private (float strength, float scale, float sea, float mountain, int floors, float terraceRatio, float holes) ReliefSettings(WFCPlanetTheme active)
        {
            float holes = active != null && active.holesAnyLevel ? active.holeScale : 0f;
            return active != null && active.overrideRelief
                ? (active.reliefStrength, active.reliefScale, active.seaFraction, active.mountainFraction, active.floors, active.terraceRatio, holes)
                : (reliefStrength, reliefScale, seaFraction, mountainFraction, floors, terraceRatio, holes);
        }

        // frozen (optionnel): piece imposee par slot (re-resolution locale: tout ce qui est hors de la zone)
        private WFCSolveResult Solve(PlanetGrid planetGrid, WFCPlanetTheme active, WFCPlanetPainting painting, int solveSeed, WFCSlotKey[] frozen)
        {
            var relief = ReliefSettings(active);
            var targets = relief.strength > 0f
                ? ReliefMap.VertexTargets(planetGrid.Points, seed, relief.scale, relief.sea, relief.mountain, relief.floors, relief.terraceRatio,
                    planetGrid.Triangles, relief.holes)
                : null;
            float bias = relief.strength;

            var constraints = painting != null ? painting.BuildConstraints(planetGrid, relief.floors, ref targets, relief.holes > 0f) : null;
            // formes booleennes (WFCPlanetShape, enfants de la planete): trous et plateaux poses dans la Scene. sans forme: rien
            WFCPlanetShape.Apply(this, planetGrid, relief.floors, painting, ref targets, ref constraints);
            // relief peint sur une planete sans carte de relief: seules les marques ont une cible
            if (constraints?.TargetWeights != null && bias <= 0f)
                bias = WFCPlanetPainting.PaintedReliefStrength;

            if (frozen != null)
            {
                constraints ??= new WFCConstraints();
                constraints.Fixed ??= new WFCSlotKey[planetGrid.SlotCount];
                for (int i = 0; i < frozen.Length; i++)
                    if (frozen[i].element != null && constraints.Fixed[i].element == null)
                        constraints.Fixed[i] = frozen[i];
            }

            return WaveFunctionCollapseManager.Instance.Solve(planetGrid.SlotNeighbors, planetGrid.Triangles, solveSeed, targets, bias,
                active != null ? active.elements : null, relief.floors, constraints);
        }

        public struct RegionSolve
        {
            public WFCSlotKey[] Solution;
            public HashSet<int> Region;
            public int Conflicts;
            public int Relaxed;
        }

        // re-resout les slots autour de `dirty` (peinture), le reste de la planete reste fige sur `current` (une piece par slot).
        // la zone s'elargit tant qu'elle n'a pas de solution sans conflit (une pente a besoin de place), 3 essais au plus.
        // salt: change le tirage (chaque coup de pinceau, bouton relancer...). ne touche pas aux stamps (cf. ApplySolution)
        public RegionSolve SolveRegion(IReadOnlyList<WFCSlotKey> current, ICollection<int> dirty, int margin, int salt)
        {
            var planetGrid = Grid;
            var result = new RegionSolve { Solution = current.ToArray(), Region = new HashSet<int>() };
            if (dirty.Count == 0 || current.Count != planetGrid.SlotCount)
                return result;

            var active = ActiveTheme;
            var painting = GetComponent<WFCPlanetPainting>();
            WFCSolveResult solve = null;
            bool log = WaveFunctionCollapseManager.LogConflicts;
            WaveFunctionCollapseManager.LogConflicts = false;
            try
            {
                for (int expansion = 0; expansion < 3; expansion++)
                {
                    result.Region = planetGrid.ExpandSlots(dirty, Mathf.Max(1, margin) << expansion);
                    var frozen = new WFCSlotKey[planetGrid.SlotCount];
                    for (int i = 0; i < frozen.Length; i++)
                        if (!result.Region.Contains(i))
                            frozen[i] = current[i];
                    solve = Solve(planetGrid, active, painting, seed * 7919 + salt * 104729 + expansion * 31, frozen);
                    if (solve.Conflicts.Count == 0)
                        break;
                }
            }
            finally
            {
                WaveFunctionCollapseManager.LogConflicts = log;
            }

            foreach (int i in result.Region)
                result.Solution[i] = solve.Key(i);
            result.Conflicts = solve.Conflicts.Count(result.Region.Contains);
            result.Relaxed = solve.Relaxed.Count(result.Region.Contains);

            var region = result.Region;
            conflictSlots.RemoveAll(region.Contains);
            conflictSlots.AddRange(solve.Conflicts.Where(region.Contains));
            LastConflictCount = conflictSlots.Count;
            RefreshConflictGizmos();
            return result;
        }

        // pose une solution sur la planete: seuls les slots dont la piece change sont reconstruits (peinture, annuler / refaire).
        // renvoie les slots reconstruits
        public List<int> ApplySolution(IReadOnlyList<WFCSlotKey> solution)
        {
            var planetGrid = Grid;
            var changed = new List<int>();
            if (solution == null || solution.Count != planetGrid.SlotCount)
                return changed;

            var instances = StampInstances();
            float floorHeight = FloorHeightOf(ActiveTheme);
            var fresh = new List<GameObject>();
            for (int i = 0; i < solution.Count; i++)
            {
                var key = solution[i];
                instances.TryGetValue(i, out var current);
                var prefab = key.element != null ? key.element.Prefab : defaultStamp;
                if (current != null ? current.key.Equals(key) : prefab == null)
                    continue;

                if (current != null)
                {
                    stampsCached.Remove(current.gameObject);
                    DestroyStamp(current.gameObject);
                }
                var inst = InstantiateSlot(i, key, floorHeight);
                if (inst != null)
                    fresh.Add(inst);
                changed.Add(i);
            }

            if (fresh.Count > 0 && enableLattice)
            {
                var around = planetGrid.TouchingSlots(changed);
                around.ExceptWith(changed);
                var neighbors = around
                    .Select(s => instances.TryGetValue(s, out var tag) ? tag : null)
                    .Where(tag => tag != null)
                    .Select(tag => tag.gameObject)
                    .ToList();
                StitchStampNormals(fresh, neighbors);
            }
            return changed;
        }

        // stamp pose sur chaque slot (les slots vides, la mer, n'en ont pas)
        public Dictionary<int, WFCStampInstance> StampInstances()
        {
            var result = new Dictionary<int, WFCStampInstance>();
            var duplicates = new List<GameObject>();
            foreach (Transform child in transform)
            {
                var tag = child.GetComponent<WFCStampInstance>();
                if (tag == null)
                    continue;
                if (result.ContainsKey(tag.slot))
                    duplicates.Add(child.gameObject);
                else
                    result[tag.slot] = tag;
            }
            foreach (var duplicate in duplicates)
            {
                stampsCached.Remove(duplicate);
                DestroyStamp(duplicate);
            }
            return result;
        }

        private void RefreshConflictGizmos()
        {
            debugConflicts.Clear();
            var planetGrid = Grid;
            foreach (int c in conflictSlots)
                if (c >= 0 && c < planetGrid.SlotCount)
                    debugConflicts.Add(transform.TransformPoint(planetGrid.Slots[c].center)); // gizmos en coordonnees monde, comme les stamps
        }

        private GameObject InstantiateSlot(int slot, WFCSlotKey key, float floorHeight)
        {
            // element sans prefab = piece vide
            var stamp = key.element != null ? key.element.Prefab : defaultStamp;
            if (stamp == null)
                return null;
            var inst = InstantiateStamp(stamp, grid.Slots[slot], key.rotation, key.level * floorHeight);
            var tag = inst.AddComponent<WFCStampInstance>();
            tag.slot = slot;
            tag.key = key;
            return inst;
        }

        #endregion

        // planete creuse: lumiere au centre, couleur du noyau. eclaire le dessous de la croute (vu par les trous) et le bas des
        // parois; le dessus du sol regarde vers l'exterieur et n'est pas eclaire. desactivee sur une planete pleine
        private const string CoreLightName = "Core Light";

        private void ApplyCoreLight(WFCPlanetTheme active)
        {
            var existing = transform.Find(CoreLightName);
            bool wanted = active != null && active.coreRadius > 0f && active.coreLightIntensity > 0f;
            if (!wanted)
            {
                if (existing != null)
                    existing.gameObject.SetActive(false);
                return;
            }

            if (existing == null)
            {
                existing = new GameObject(CoreLightName, typeof(Light)).transform;
                existing.SetParent(transform, false);
            }
            existing.gameObject.SetActive(true);
            existing.localPosition = Vector3.zero;
            var coreLight = existing.GetComponent<Light>();
            coreLight.type = LightType.Point;
            coreLight.color = active.seaColor;
            coreLight.intensity = active.coreLightIntensity;
            coreLight.range = radius * Mathf.Max(transform.lossyScale.x, 0.001f) * 1.2f;
            coreLight.shadows = LightShadows.None;
        }

        // material du theme (lave...), sinon on remet celui de la planete (changement de theme en jeu)
        private void ApplySeaMaterial(WFCPlanetTheme active)
        {
            var meshRenderer = GetComponent<MeshRenderer>();
            if (active != null && active.seaMaterial != null)
            {
                if (planetMaterial == null)
                    planetMaterial = meshRenderer.sharedMaterial;
                meshRenderer.sharedMaterial = active.seaMaterial;
            }
            else if (planetMaterial != null)
            {
                meshRenderer.sharedMaterial = planetMaterial;
                planetMaterial = null;
            }
        }

        private static Mesh BuildFlatMesh(List<Vector3> vertices, List<int> triangles, List<Color> colors)
        {
            var mesh = new Mesh { name = GeneratedMeshName };
            if (vertices.Count > 65534)
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // mer (niveau 0): chaque triangle de la grille est subdivise et ramene sur la sphere, meme courbure que les stamps.
        // les sommets poses sur une arete sont partages avec le triangle voisin (pas de fente)
        // colorAt (optionnel): couleur de chaque sommet genere, sinon interpolee entre les sommets de la grille
        // radiusScale: rayon de la mer par rapport a la grille (< 1: fond des gouffres)
        private static Mesh BuildSeaMesh(List<Vector3> points, List<int[]> triangles, List<Color> colors, int subdivisions,
            System.Func<Vector3, Color> colorAt = null, float radiusScale = 1f)
        {
            float sphereRadius = points.Average(p => p.magnitude) * radiusScale;
            var vertices = new List<Vector3>();
            var vertexColors = new List<Color>();
            var indices = new List<int>();
            var shared = new Dictionary<(int, int, int, int, int, int), int>();

            int Vertex(int[] t, int i, int j)
            {
                // poids entiers des 3 sommets; la cle ne garde que les poids non nuls, tries par sommet
                var weights = new List<(int id, int w)> { (t[0], subdivisions - i - j), (t[1], i), (t[2], j) };
                weights.RemoveAll(x => x.w == 0);
                weights.Sort((x, y) => x.id.CompareTo(y.id));
                while (weights.Count < 3)
                    weights.Add((-1, 0));
                var key = (weights[0].id, weights[0].w, weights[1].id, weights[1].w, weights[2].id, weights[2].w);
                if (shared.TryGetValue(key, out int index))
                    return index;

                Vector3 p = Vector3.zero;
                Color c = Color.clear;
                foreach (var (id, w) in weights)
                {
                    if (id < 0) continue;
                    p += points[id] * w;
                    c += colors[id] * w;
                }
                index = vertices.Count;
                var position = p.normalized * sphereRadius;
                vertices.Add(position);
                vertexColors.Add(colorAt != null ? colorAt(position) : c / subdivisions);
                shared[key] = index;
                return index;
            }

            foreach (var t in triangles)
            {
                for (int i = 0; i < subdivisions; i++)
                {
                    for (int j = 0; j < subdivisions - i; j++)
                    {
                        indices.Add(Vertex(t, i, j));
                        indices.Add(Vertex(t, i + 1, j));
                        indices.Add(Vertex(t, i, j + 1));
                        if (i + j + 1 < subdivisions)
                        {
                            indices.Add(Vertex(t, i + 1, j));
                            indices.Add(Vertex(t, i + 1, j + 1));
                            indices.Add(Vertex(t, i, j + 1));
                        }
                    }
                }
            }

            return BuildFlatMesh(vertices, indices, vertexColors);
        }
        
        // lift: decalage vertical du stamp en hauteurs de cage (etages, 0 = modelise au niveau de la mer)
        private GameObject InstantiateStamp(GameObject stamp, WFCSlot slot, int rotation, float lift)
        {
            var cellVerts = grid.Points;
            // le "haut" du stamp pointe vers le sommet qui recoit la pointe du mesh,
            // FreeFormDeformer.OrderContainerPairs associe ensuite les ancres par proximite
            var corners = new[] { cellVerts[slot.o], cellVerts[slot.a], cellVerts[slot.b] };
            Vector3 surfaceUp = (corners[rotation] - slot.center).normalized;
            if (surfaceUp.sqrMagnitude < 0.001f) surfaceUp = Vector3.up;
            var orientation = Quaternion.LookRotation(slot.center.normalized, surfaceUp);

            // enfant de la planete, en coordonnees monde: la planete peut etre deplacee ou tournee (plusieurs planetes par scene)
            var inst = Instantiate(stamp, transform.TransformPoint(slot.center), transform.rotation * orientation, transform);

            var FFDContainer = new List<Vector3>()
            {
                transform.TransformPoint(cellVerts[slot.o]),
                transform.TransformPoint(cellVerts[slot.a]),
                transform.TransformPoint(cellVerts[slot.b]),
                transform.TransformPoint(cellVerts[slot.o] + cellVerts[slot.o] * layerHeight),
                transform.TransformPoint(cellVerts[slot.a] + cellVerts[slot.a] * layerHeight),
                transform.TransformPoint(cellVerts[slot.b] + cellVerts[slot.b] * layerHeight)
            };
            // debug seulement: la peinture reconstruit des stamps sans fin, la liste grossirait a chaque trait
            if (enableLatticeDebug)
                debugVirtualPoint.AddRange(FFDContainer);

            // sol (Render) + decor/eau (Decor, hors collision): meme cage pour tous les meshes du stamp
            var ffdElement = inst.GetComponentInChildren<FreeFormDeformerElement>();
            foreach (var meshFilter in inst.GetComponentsInChildren<MeshFilter>())
            {
                // piece qui n'a que du decor (rocher qui flotte au-dessus du vide): pas de sol
                if (meshFilter.sharedMesh == null)
                    continue;
                var meshInstance = Instantiate(meshFilter.sharedMesh);
                meshInstance.name = meshFilter.sharedMesh.name + InstanceSuffix;
                ownedMeshes.Add(meshInstance);
                if (enableLattice)
                    FreeFormDeformer.Init(FFDContainer, ffdElement.GetRelativeAnchors(), meshInstance, meshFilter.transform,
                        followCurvature ? transform.position : (Vector3?)null, lift);
                meshFilter.sharedMesh = meshInstance;
            }

            stampsCached.Add(inst);
            if (enableLatticeDebug)
                _debugCellsDatas.Add(new DEBUG_cellsData(slot.center, orientation.eulerAngles, rotation));
            return inst;
        }

        // chaque stamp recalcule ses normales seul (FFD): sur les aretes partagees on moyenne les deux cotes, sinon on voit les jointures.
        // uniquement les sols, le decor garde ses aretes dures.
        // fresh: stamps qui viennent d'etre deformes (normales brutes). neighbors (optionnel): stamps deja cousus autour d'eux, leurs
        // normales brutes sont recalculees sur une copie. seules les positions des sommets des stamps fresh sont recousues
        private void StitchStampNormals(IEnumerable<GameObject> fresh, IEnumerable<GameObject> neighbors)
        {
            static IEnumerable<MeshFilter> Grounds(IEnumerable<GameObject> stamps) => stamps
                .Where(s => s != null)
                .SelectMany(s => s.GetComponentsInChildren<MeshFilter>())
                .Where(mf => mf.name != DecorChildName && mf.sharedMesh != null);

            var freshGrounds = Grounds(fresh)
                .Select(mf => (filter: mf, vertices: mf.sharedMesh.vertices, normals: mf.sharedMesh.normals))
                .ToList();
            var neighborGrounds = neighbors == null
                ? new List<(MeshFilter filter, Vector3[] vertices, Vector3[] normals, Vector3[] raw)>()
                : Grounds(neighbors)
                    .Select(mf => (filter: mf, vertices: mf.sharedMesh.vertices, normals: mf.sharedMesh.normals, raw: RawNormals(mf.sharedMesh)))
                    .ToList();

            // positions dans le repere de la planete: la tolerance suit le rayon, quels que soient position, rotation et echelle.
            // les deux copies d'un sommet partage different de quelques 1e-6 (FFD de deux stamps): pres d'un bord de cellule l'arrondi
            // les mettait dans deux cellules voisines (jointure non cousue). une position proche d'un bord reprend donc la cellule
            // voisine deja vue de ce cote
            float cellSize = Mathf.Max(radius * 1e-3f, 1e-5f);
            const float nearBorder = 0.4f; // fraction de cellule a partir de laquelle on regarde la cellule voisine
            var cells = new Dictionary<Vector3Int, Vector3Int>();
            Vector3Int Key(Matrix4x4 toPlanet, Vector3 vertex)
            {
                var scaled = toPlanet.MultiplyPoint3x4(vertex) / cellSize;
                var cell = Vector3Int.RoundToInt(scaled);
                if (cells.TryGetValue(cell, out var known))
                    return known;
                var offset = scaled - (Vector3)cell;
                int sx = offset.x > nearBorder ? 1 : offset.x < -nearBorder ? -1 : 0;
                int sy = offset.y > nearBorder ? 1 : offset.y < -nearBorder ? -1 : 0;
                int sz = offset.z > nearBorder ? 1 : offset.z < -nearBorder ? -1 : 0;
                for (int dx = 0; dx <= Mathf.Abs(sx); dx++)
                for (int dy = 0; dy <= Mathf.Abs(sy); dy++)
                for (int dz = 0; dz <= Mathf.Abs(sz); dz++)
                {
                    if (dx + dy + dz == 0)
                        continue;
                    if (cells.TryGetValue(cell + new Vector3Int(dx * sx, dy * sy, dz * sz), out known))
                    {
                        cells[cell] = known;
                        return known;
                    }
                }
                cells[cell] = cell;
                return cell;
            }
            var worldToPlanet = transform.worldToLocalMatrix;

            // normales brutes (repere monde) de tous les sommets, chainees par position
            var head = new Dictionary<Vector3Int, int>();
            var raws = new List<Vector3>();
            var next = new List<int>();
            void Add(Vector3Int key, Vector3 normal)
            {
                next.Add(head.TryGetValue(key, out int first) ? first : -1);
                head[key] = raws.Count;
                raws.Add(normal);
            }
            // aretes dures voulues: un meme stamp a plusieurs sommets a cette position (separes par l'import: couleur par face,
            // contremarche d'un escalier). la, on ne moyenne que les normales proches (angle de lissage): les marches restent
            // nettes. ailleurs on moyenne tout, comme avant (pointe d'une montagne: chaque stamp n'en voit qu'un cote)
            var hard = new HashSet<Vector3Int>();
            float minDot = Mathf.Cos(StitchSmoothAngle * Mathf.Deg2Rad);
            Vector3 Smooth(Vector3Int key, Vector3 normal)
            {
                bool split = hard.Contains(key);
                var sum = Vector3.zero;
                for (int j = head[key]; j >= 0; j = next[j])
                    if (!split || Vector3.Dot(raws[j], normal) >= minDot)
                        sum += raws[j];
                return sum.sqrMagnitude > 1e-12f ? sum.normalized : normal;
            }
            Vector3Int[] Keys(MeshFilter filter, Vector3[] vertices)
            {
                var toPlanet = worldToPlanet * filter.transform.localToWorldMatrix;
                var keys = new Vector3Int[vertices.Length];
                var seen = new HashSet<Vector3Int>();
                for (int i = 0; i < vertices.Length; i++)
                {
                    keys[i] = Key(toPlanet, vertices[i]);
                    if (!seen.Add(keys[i]))
                        hard.Add(keys[i]);
                }
                return keys;
            }

            var freshData = new List<(Vector3Int[] keys, Vector3[] world)>();
            foreach (var (filter, vertices, normals) in freshGrounds)
            {
                var tr = filter.transform;
                var keys = Keys(filter, vertices);
                var world = new Vector3[vertices.Length];
                for (int i = 0; i < vertices.Length; i++)
                {
                    world[i] = tr.TransformDirection(normals[i]);
                    Add(keys[i], world[i]);
                }
                freshData.Add((keys, world));
            }
            // les voisins ne comptent que sur les positions partagees avec un stamp neuf
            var shared = new HashSet<Vector3Int>(head.Keys);
            var neighborData = new List<(Vector3Int[] keys, Vector3[] world)>();
            foreach (var (filter, vertices, _, raw) in neighborGrounds)
            {
                var tr = filter.transform;
                var keys = Keys(filter, vertices);
                var world = new Vector3[vertices.Length];
                for (int i = 0; i < vertices.Length; i++)
                {
                    world[i] = tr.TransformDirection(raw[i]);
                    if (shared.Contains(keys[i]))
                        Add(keys[i], world[i]);
                }
                neighborData.Add((keys, world));
            }

            for (int g = 0; g < freshGrounds.Count; g++)
            {
                var (filter, vertices, normals) = freshGrounds[g];
                var (keys, world) = freshData[g];
                for (int i = 0; i < vertices.Length; i++)
                    normals[i] = filter.transform.InverseTransformDirection(Smooth(keys[i], world[i]));
                filter.sharedMesh.normals = normals;
            }
            for (int g = 0; g < neighborGrounds.Count; g++)
            {
                var (filter, vertices, normals, _) = neighborGrounds[g];
                var (keys, world) = neighborData[g];
                bool touched = false;
                for (int i = 0; i < vertices.Length; i++)
                {
                    if (!shared.Contains(keys[i]))
                        continue;
                    normals[i] = filter.transform.InverseTransformDirection(Smooth(keys[i], world[i]));
                    touched = true;
                }
                if (touched)
                    filter.sharedMesh.normals = normals;
            }
        }

        // angle de lissage des jointures la ou un stamp a une arete dure (contremarche a 55 deg et plus: reste nette)
        private const float StitchSmoothAngle = 40f;

        // normales du mesh deforme avant couture
        private static Vector3[] RawNormals(Mesh mesh)
        {
            var copy = Instantiate(mesh);
            copy.RecalculateNormals();
            var normals = copy.normals;
            DestroyImmediate(copy);
            return normals;
        }

        // le plus grand nombre entier qui divise simultanément ces deux nombres
        private int PGCD (int x, int y) {
            int r = x % y;
            if (r == 0) {
                return y;
            }
            return PGCD(y, r);
        }
        

        // toujours visible pour la live preview (on edite les regles sans selectionner la planete)
        private void OnDrawGizmos()
        {
            Gizmos.color = Color.red;
            foreach (var p in debugConflicts)
                Gizmos.DrawSphere(p, 0.08f);
        }

        private void OnDrawGizmosSelected()
        {
            for (int j = 0; j < debugVirtualPoint.Count; j++)
            {
                Gizmos.color = Color.crimson;
                Gizmos.DrawCube(debugVirtualPoint[j], Vector3.one * 0.02f);
            }
            if (_debugCellsDatas
                    .Count == 0)
                return;
                
            for (int i = 0; i < _debugCellsDatas.Count; i++)
            {
                var el = _debugCellsDatas[i];
                // WFCUtils.DrawString(i+"."+el.extra, el.center, Color.white);
            }

            // for (int i = 0; i < FreeFormDeformer.DebugValue.Count; i++)
            // {
            //     var el = FreeFormDeformer.DebugValue.ElementAt(i);
            //     
            //     Debug.DrawLine(el.Item1, el.Item2, Color.red);
            // }

            if (!enableLatticeDebug)
                return;
            Gizmos.color = Color.cyan;
            for (int i = 0; i < FreeFormDeformer.DebugGrid.Count; i++)
            {
                var el = FreeFormDeformer.DebugGrid[i];
                Gizmos.DrawLine(el.Item1, el.Item2);
            }
        }

    }
}

public static class GPUtils
{
    public static float R3 = Mathf.Sqrt(3);
    public static float THRDR3 = R3 / 3f;
    public static float GRIDSIZE = 0.5f;
    
    public static Vector2Int Rotate120Sides(this Vector2Int v, int m, int n)
    {
        int x = v.x;

        return new Vector2Int(
            m - x - v.y,
            n + x
        );
    }
    
    public static Vector2Int rotateNeg120Sides(this Vector2Int v, int m, int n)
    {
        int x = v.x;

        return new Vector2Int(
            v.y - n,
            m + n - x - v.y
        );
    }
    
    public static Vector3 toCartesianOrigin (this Vector2Int v, Vector2Int origin) {
        var point = Vector3.zero;
        point.x = origin.x + 2 * v.x * GRIDSIZE + v.y * GRIDSIZE;
        point.y = origin.y + 3 * THRDR3 * v.y * GRIDSIZE;
        return point;
    }
            
    // editor only (scene view), ne fait rien en build
    public static void DrawString(
        string text,
        Vector3 worldPos,
        Color? textColor = null,
        Color? backColor = null)
    {
#if UNITY_EDITOR
        var view = UnityEditor.SceneView.currentDrawingSceneView;
    
        if (view == null || view.camera == null)
            return;
    
        UnityEditor.Handles.BeginGUI();
    
        Color restoreTextColor = GUI.color;
        Color restoreBackColor = GUI.backgroundColor;
    
        try
        {
            GUI.color = textColor ?? Color.white;
            GUI.backgroundColor = backColor ?? Color.black;
    
            Camera camera = view.camera;
            Vector3 origin = camera.transform.position;
            Vector3 direction = worldPos - origin;
            float distance = direction.magnitude;

            if (Physics.Raycast(origin, direction.normalized, out RaycastHit hit, distance))
            {
                // pas trouver mieux en terme de perf.
                return;
            }
            
            Vector3 screenPos = camera.WorldToScreenPoint(worldPos);
    
            // outside viewport (il y a un truc si cinemachine integrer)
            if (screenPos.z <= 0f ||
                screenPos.x < 0f ||
                screenPos.x > camera.pixelWidth ||
                screenPos.y < 0f ||
                screenPos.y > camera.pixelHeight)
            {
                return;
            }
    
            Vector2 size = GUI.skin.label.CalcSize(new GUIContent(text));
    
            // Unity GUI uses a top-left origin while ScreenToWorldPoint
            // WorldToScreenPoint uses a bottom-left origin. -;-
            float guiY = camera.pixelHeight - screenPos.y;
    
            Rect rect = new Rect(
                screenPos.x - size.x * 0.5f,
                guiY + 4f,
                size.x,
                size.y
            );
    
            GUI.Box(rect, text, EditorStyles.numberField);
            GUI.Label(rect, text);
        }
        finally
        {
            GUI.color = restoreTextColor;
            GUI.backgroundColor = restoreBackColor;
    
            UnityEditor.Handles.EndGUI();
        }
#endif
    }
}
