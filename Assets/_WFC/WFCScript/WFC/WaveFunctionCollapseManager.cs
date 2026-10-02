using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WFCContent.HE;

namespace WFCContent.WFC
{
    public class WFCSolveResult
    {
        public WFCElementData[] Assignment;
        // index du sommet du slot (0 = centre cellule, 1 = a, 2 = b) ou va la pointe du mesh
        public int[] Rotations;
        // decalage de la piece en etages (0 = modelisee au niveau de la mer, 1 = posee un etage plus haut...)
        public int[] Levels;
        public List<int> Conflicts = new();
        // slots dont les contraintes dures (peinture) ne laissaient aucune piece: resolus sans elles
        public List<int> Relaxed = new();
        public int Attempts;

        public WFCSlotKey Key(int slot) => new(Assignment[slot], Rotations[slot], Levels[slot]);
    }

    public class WaveFunctionCollapseManager: HESingleton<WaveFunctionCollapseManager>
    {
        [SerializeField, Range(1, 50)] private int maxAttempts = 10;

        // une piece dans une rotation et a un etage donnes
        private struct Variant
        {
            public int element;
            public int rotation;
            public int level; // decalage en etages
            public int[] heights; // hauteur aux sommets du slot [o, a, b], decalage compris
            public WFCEdge[] edges; // aretes du slot [o-a, a-b, b-o]
            public float weight;
        }

        private struct Link
        {
            public int other;
            public bool[,] compat; // compat[variant ici, variant chez other]
        }

        // neighbors[i] = slots voisins de i, slotVertices[i] = id globaux des sommets [o, a, b] du slot i
        // vertexTargets (optionnel): hauteur souhaitee par sommet (carte de relief). Ne change pas les regles, seulement les
        // probabilites: le poids d'une variante est multiplie par exp(-targetBias * somme des (hauteur - cible)^2) sur ses 3 sommets
        // elements (optionnel): stamps du theme de la planete, null = stamps de la WFCDatabase ranges dans aucun theme
        // (tous tant qu'il n'y a pas de theme)
        // floors: etages de terrain au-dessus de la mer, les pieces sans mer sont aussi essayees 1..floors-1 etages plus haut
        // constraints (optionnel): peinture de la planete et slots figes (re-resolution locale), cf. WFCConstraints
        public WFCSolveResult Solve(IReadOnlyList<List<int>> neighbors, IReadOnlyList<int[]> slotVertices, int seed,
            IReadOnlyList<float> vertexTargets = null, float targetBias = 0f, IReadOnlyList<WFCElementData> elements = null,
            int floors = 1, WFCConstraints constraints = null)
        {
            var database = WFCDatabase.Instance;
            var pieces = (elements ?? database.UnthemedElements())
                .Where(e => e != null)
                .Distinct()
                .ToList();
            if (elements == null && pieces.Count > 0 && pieces.All(p => p.Prefab == null) && database.Themes.Any(t => t != null))
                Debug.LogWarning("[WFC] Planete sans theme et tous les stamps sont ranges dans des themes: il ne reste que la mer. " +
                                 "Choisis un theme sur la planete (ou defaultTheme dans la WFCDatabase).");

            int count = neighbors.Count;
            var result = new WFCSolveResult
            {
                Assignment = new WFCElementData[count],
                Rotations = new int[count],
                Levels = new int[count]
            };
            if (pieces.Count == 0)
            {
                Debug.LogError(elements != null ? "[WFC] Aucun element dans le theme de la planete." : "[WFC] Aucun element dans la WFCDatabase.");
                return result;
            }

            floors = Mathf.Max(1, floors);
            var variants = BuildVariants(pieces, floors);
            if (floors > 1 && !variants.Any(v => v.heights.Max() == 2 && v.heights.Min() == 1 && !pieces[v.element].peak))
                Debug.LogWarning($"[WFC] Planete a {floors} etages sans pente entre deux etages (piece (2,1,1) ou (1,2,2) non montagne): " +
                                 "les plateaux ne peuvent pas apparaitre. Importe les stamps RAMP_* du generateur.");
            var links = BuildLinks(neighbors, slotVertices, variants, pieces);
            var start = InitialDomains(slotVertices, variants, pieces, constraints, result.Relaxed);
            var factors = SlotFactors(count, pieces, constraints);

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                bool lastAttempt = attempt == maxAttempts - 1;
                result.Attempts = attempt + 1;
                result.Conflicts.Clear();

                var bias = vertexTargets != null && targetBias > 0f
                    ? new TargetBias(slotVertices, vertexTargets, targetBias, constraints?.TargetWeights)
                    : null;
                var picked = TrySolve(links, variants, new System.Random(seed + attempt * 7919), lastAttempt, result.Conflicts, bias,
                    start, factors);
                if (picked == null)
                    continue;

                for (int i = 0; i < count; i++)
                {
                    result.Assignment[i] = pieces[variants[picked[i]].element];
                    result.Rotations[i] = variants[picked[i]].rotation;
                    result.Levels[i] = variants[picked[i]].level;
                }
                break;
            }

            if (result.Conflicts.Count > 0 && LogConflicts)
                Debug.LogWarning($"[WFC] {result.Conflicts.Count} slot(s) sans solution apres {result.Attempts} tentatives, regles trop strictes ?");

            return result;
        }

        // la peinture re-resout souvent de petites zones et reessaie plus large en cas d'echec: elle gere ses propres messages
        public static bool LogConflicts = true;

        // domaine de depart de chaque slot: toutes les variantes, sauf contraintes (peinture, slots figes)
        private static StartDomains InitialDomains(IReadOnlyList<int[]> slotVertices, List<Variant> variants, List<WFCElementData> pieces,
            WFCConstraints constraints, List<int> relaxed)
        {
            int count = slotVertices.Count;
            var start = new StartDomains { Domains = new List<int>[count], Frozen = new bool[count] };
            if (constraints == null)
                return start; // null = toutes les variantes
            start.Priority = constraints.Priority;

            var index = new Dictionary<WFCElementData, int>();
            for (int e = 0; e < pieces.Count; e++)
                index[pieces[e]] = e;

            for (int s = 0; s < count; s++)
            {
                var vertices = slotVertices[s];

                // piece figee: une seule variante (meme hauteurs et aretes que la rotation demandee)
                var fixedKey = constraints.Fixed != null ? constraints.Fixed[s] : default;
                if (fixedKey.element != null && index.TryGetValue(fixedKey.element, out int fixedElement))
                {
                    int match = FindVariant(variants, fixedElement, fixedKey.rotation, fixedKey.level, fixedKey.element);
                    if (match >= 0)
                    {
                        start.Domains[s] = new List<int> { match };
                        start.Frozen[s] = true;
                        continue;
                    }
                }

                var allowed = constraints.Allowed?[s];
                int rotation = constraints.AllowedRotation != null ? constraints.AllowedRotation[s] : -1;
                var weights = constraints.Weights?[s];
                bool heights = false;
                if (constraints.VertexHeights != null)
                    foreach (int v in vertices)
                        heights |= constraints.VertexHeights[v] >= 0;
                if (allowed == null && weights == null && !heights)
                    continue;

                // du plus important au moins important: si rien ne passe on lache d'abord les interdits de zone, puis les stamps
                // peints, puis les hauteurs peintes
                for (int strictness = 3; strictness >= 0; strictness--)
                {
                    var domain = new List<int>();
                    for (int v = 0; v < variants.Count; v++)
                    {
                        var variant = variants[v];
                        var element = pieces[variant.element];
                        if (strictness >= 1 && heights && !MatchesHeights(variant, vertices, constraints.VertexHeights))
                            continue;
                        if (strictness >= 2 && allowed != null &&
                            (!allowed.Contains(element) || (rotation >= 0 && !IsRotation(variant, element, rotation))))
                            continue;
                        if (strictness >= 3 && weights != null && weights(element) <= 0f)
                            continue;
                        domain.Add(v);
                    }

                    if (domain.Count > 0)
                    {
                        start.Domains[s] = domain;
                        if (strictness < 3)
                            relaxed.Add(s);
                        break;
                    }
                }
            }

            return start;
        }

        private static bool MatchesHeights(Variant variant, int[] vertices, int[] vertexHeights)
        {
            for (int k = 0; k < 3; k++)
            {
                int wanted = vertexHeights[vertices[k]];
                if (wanted >= 0 && variant.heights[k] != wanted)
                    return false;
            }
            return true;
        }

        // la variante a les hauteurs et aretes de la piece tournee de `rotation` (les rotations identiques sont fusionnees)
        private static bool IsRotation(Variant variant, WFCElementData element, int rotation)
        {
            var source = element.CornerHeights;
            var sourceEdges = element.Edges;
            for (int i = 0; i < 3; i++)
            {
                int k = (i + rotation) % 3;
                if (variant.heights[k] - variant.level != source[i] || variant.edges[k] != sourceEdges[i])
                    return false;
            }
            return true;
        }

        private static int FindVariant(List<Variant> variants, int element, int rotation, int level, WFCElementData data)
        {
            for (int v = 0; v < variants.Count; v++)
                if (variants[v].element == element && variants[v].level == level && IsRotation(variants[v], data, rotation))
                    return v;
            return -1;
        }

        // multiplicateur de poids par slot et par piece (zones peintes), null = 1 partout
        private static float[][] SlotFactors(int count, List<WFCElementData> pieces, WFCConstraints constraints)
        {
            if (constraints?.Weights == null)
                return null;
            var factors = new float[count][];
            for (int s = 0; s < count; s++)
            {
                var weights = constraints.Weights[s];
                if (weights == null)
                    continue;
                factors[s] = new float[pieces.Count];
                for (int e = 0; e < pieces.Count; e++)
                    factors[s][e] = Mathf.Max(0f, weights(pieces[e]));
            }
            return factors;
        }

        private sealed class StartDomains
        {
            public List<int>[] Domains; // null = toutes les variantes
            public bool[] Frozen;
            public bool[] Priority; // null = aucune
        }

        private static List<Variant> BuildVariants(List<WFCElementData> elements, int floors)
        {
            var variants = new List<Variant>();
            for (int e = 0; e < elements.Count; e++)
            {
                var el = elements[e];
                var source = el.CornerHeights;
                var sourceEdges = el.Edges;
                int rotations = el.allowRotation ? 3 : 1;

                // une piece aux 3 coins (et 3 aretes) identiques n'a qu'une seule variante utile
                var unique = new List<(int rotation, int[] heights, WFCEdge[] edges)>();
                for (int r = 0; r < rotations; r++)
                {
                    var heights = new int[3];
                    var edges = new WFCEdge[3];
                    for (int i = 0; i < 3; i++)
                    {
                        heights[(i + r) % 3] = source[i];
                        edges[(i + r) % 3] = sourceEdges[i]; // l'arete i suit son sommet de depart
                    }
                    if (unique.Any(u => u.heights.SequenceEqual(heights) && u.edges.SequenceEqual(edges)))
                        continue;
                    unique.Add((r, heights, edges));
                }

                // le poids est reparti entre les rotations pour garder le poids total de la piece, a chaque etage
                // (la carte de relief choisit l'etage)
                foreach (int level in el.Levels(floors))
                    foreach (var (rotation, heights, edges) in unique)
                        variants.Add(new Variant
                        {
                            element = e, rotation = rotation, level = level, edges = edges, weight = el.weight / unique.Count,
                            heights = heights.Select(h => h + level).ToArray()
                        });
            }
            return variants;
        }

        private static List<Link>[] BuildLinks(IReadOnlyList<List<int>> neighbors, IReadOnlyList<int[]> slotVertices, List<Variant> variants, List<WFCElementData> elements)
        {
            int n = variants.Count;
            var elementCompat = new bool[elements.Count, elements.Count];
            for (int a = 0; a < elements.Count; a++)
                for (int b = 0; b < elements.Count; b++)
                    elementCompat[a, b] = WFCElementData.AreCompatible(elements[a], elements[b]);

            // meme combinaison de sommets partages = meme matrice, on cache
            var cache = new Dictionary<int, bool[,]>();
            var links = new List<Link>[neighbors.Count];

            for (int s = 0; s < neighbors.Count; s++)
            {
                links[s] = new List<Link>();
                foreach (int t in neighbors[s])
                {
                    var shared = new List<(int mine, int theirs)>();
                    int key = 0;
                    for (int i = 0; i < 3; i++)
                        for (int j = 0; j < 3; j++)
                            if (slotVertices[s][i] == slotVertices[t][j])
                            {
                                shared.Add((i, j));
                                key |= 1 << (i * 3 + j);
                            }

                    if (!cache.TryGetValue(key, out var compat))
                    {
                        compat = new bool[n, n];
                        for (int x = 0; x < n; x++)
                            for (int y = 0; y < n; y++)
                            {
                                bool ok = elementCompat[variants[x].element, variants[y].element];
                                foreach (var (mine, theirs) in shared)
                                    ok &= variants[x].heights[mine] == variants[y].heights[theirs];
                                // 2 sommets partages = une arete partagee, meme contenu des deux cotes
                                if (shared.Count == 2)
                                    ok &= variants[x].edges[EdgeIndex(shared[0].mine, shared[1].mine)] ==
                                          variants[y].edges[EdgeIndex(shared[0].theirs, shared[1].theirs)];
                                compat[x, y] = ok;
                            }
                        cache[key] = compat;
                    }

                    links[s].Add(new Link { other = t, compat = compat });
                }
            }

            return links;
        }

        // sommets {0,1} -> arete 0, {1,2} -> arete 1, {2,0} -> arete 2
        private static int EdgeIndex(int i, int j) => (i + j) switch { 1 => 0, 3 => 1, _ => 2 };

        // ponderation par la carte de relief: facteur de chaque variante pour un slot donne
        private sealed class TargetBias
        {
            private readonly IReadOnlyList<int[]> slotVertices;
            private readonly IReadOnlyList<float> targets;
            private readonly float strength;
            // poids de la cible de chaque sommet (peinture), null = 1 partout. 0 = sommet sans cible
            private readonly float[] weights;

            public TargetBias(IReadOnlyList<int[]> slotVertices, IReadOnlyList<float> targets, float strength, float[] weights = null)
            {
                this.slotVertices = slotVertices;
                this.targets = targets;
                this.strength = strength;
                this.weights = weights;
            }

            public float Error(int slot, Variant variant)
            {
                float error = 0f;
                var vertices = slotVertices[slot];
                for (int k = 0; k < 3; k++)
                {
                    if (weights != null)
                    {
                        float w = weights[vertices[k]];
                        if (w <= 0f)
                            continue;
                        float dw = variant.heights[k] - targets[vertices[k]];
                        error += w * dw * dw;
                        continue;
                    }
                    float d = variant.heights[k] - targets[vertices[k]];
                    error += d * d;
                }
                return error;
            }

            // rapporte au meilleur candidat du slot (bestError): meme proportions, mais exp ne tombe plus a 0 en float
            // (avec plusieurs etages l'ecart depasse vite 15: tous les poids valaient 0 et le tirage devenait uniforme)
            public float Factor(int slot, Variant variant, float bestError) => Mathf.Exp(-strength * (Error(slot, variant) - bestError));
        }

        // null = contradiction, sauf si forceComplete ou on note le conflit et on continue
        // start: domaines de depart (contraintes) et slots figes: ils ne recoivent pas la propagation, leurs voisins s'y adaptent
        // factors: multiplicateur de poids par slot et par piece (zones peintes)
        private static int[] TrySolve(List<Link>[] links, List<Variant> variants, System.Random rng, bool forceComplete, List<int> conflicts,
            TargetBias bias, StartDomains start, float[][] factors)
        {
            int count = links.Length;
            int n = variants.Count;
            var domains = new List<int>[count];
            for (int i = 0; i < count; i++)
                domains[i] = start.Domains[i] != null ? new List<int>(start.Domains[i]) : Enumerable.Range(0, n).ToList();
            var frozen = start.Frozen;

            // un slot fige entoure de slots figes n'a rien a propager (re-resolution locale: presque toute la planete)
            var stack = new Stack<int>(Enumerable.Range(0, count).Where(i => !frozen[i] || links[i].Exists(l => !frozen[l.other])));
            if (!Propagate(stack, domains, links, forceComplete, conflicts, frozen))
                return null;

            var priority = start.Priority;
            while (true)
            {
                // plus petite entropie (taille du domaine), egalite -> random. les slots prioritaires (peinture) passent d'abord
                int best = -1;
                int bestSize = int.MaxValue;
                int ties = 0;
                bool bestPriority = false;
                for (int i = 0; i < count; i++)
                {
                    int size = domains[i].Count;
                    if (size <= 1) continue;
                    if (priority != null)
                    {
                        if (priority[i] && !bestPriority)
                        {
                            best = i;
                            bestSize = size;
                            ties = 1;
                            bestPriority = true;
                            continue;
                        }
                        if (!priority[i] && bestPriority)
                            continue;
                    }
                    if (size < bestSize)
                    {
                        best = i;
                        bestSize = size;
                        ties = 1;
                    }
                    else if (size == bestSize && rng.Next(++ties) == 0)
                    {
                        best = i;
                    }
                }

                if (best == -1)
                    break;

                int choice = PickWeighted(domains[best], variants, rng, bias, best, factors?[best]);
                domains[best].Clear();
                domains[best].Add(choice);

                stack.Push(best);
                if (!Propagate(stack, domains, links, forceComplete, conflicts, frozen))
                    return null;
            }

            var picked = new int[count];
            for (int i = 0; i < count; i++)
                picked[i] = domains[i][0];
            return picked;
        }

        private static int PickWeighted(List<int> domain, List<Variant> variants, System.Random rng, TargetBias bias, int slot, float[] factors)
        {
            float bestError = 0f;
            if (bias != null)
            {
                bestError = float.MaxValue;
                foreach (int v in domain)
                    bestError = Mathf.Min(bestError, bias.Error(slot, variants[v]));
            }
            float BaseWeight(int v) => bias != null ? variants[v].weight * bias.Factor(slot, variants[v], bestError) : variants[v].weight;
            float Weight(int v) => factors != null ? BaseWeight(v) * factors[variants[v].element] : BaseWeight(v);

            float total = 0f;
            foreach (int v in domain)
                total += Weight(v);
            if (total <= 0f)
                return domain[rng.Next(domain.Count)];

            float roll = (float)rng.NextDouble() * total;
            foreach (int v in domain)
            {
                roll -= Weight(v);
                if (roll <= 0f)
                    return v;
            }
            return domain[domain.Count - 1];
        }

        private static bool Propagate(Stack<int> stack, List<int>[] domains, List<Link>[] links, bool forceComplete, List<int> conflicts,
            bool[] frozen)
        {
            while (stack.Count > 0)
            {
                int current = stack.Pop();
                var currentDomain = domains[current];

                foreach (var link in links[current])
                {
                    int nb = link.other;
                    if (frozen[nb])
                        continue;
                    var compat = link.compat;
                    bool Supported(int candidate) => currentDomain.Any(c => compat[c, candidate]);

                    if (!domains[nb].Exists(Supported))
                    {
                        if (!forceComplete)
                            return false;

                        // derniere tentative: on accepte la violation pour avoir quand meme un resultat. le voisin garde son
                        // domaine: le rouvrir en entier le faisait retirer, il cassait ses voisins, etc. (boucle sans fin)
                        if (!conflicts.Contains(nb))
                            conflicts.Add(nb);
                        continue;
                    }

                    // retire du voisin tout ce qui n'a aucun partenaire compatible dans current
                    if (domains[nb].RemoveAll(candidate => !Supported(candidate)) > 0)
                        stack.Push(nb);
                }
            }

            return true;
        }
    }
}
