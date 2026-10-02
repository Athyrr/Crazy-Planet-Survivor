using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace WFCContent.World
{
    // carte de relief facon Planetary Annihilation: une hauteur cible par sommet de la grille (0 mer, 1..floors etages,
    // floors + 1 montagne). le WFC reste strict (hauteurs, sockets), la carte ne fait que ponderer ses choix: sans elle le WFC
    // ne fait que des cotes et des petites iles (il avance la ou il y a le moins d'options, donc depuis la mer)
    public static class ReliefMap
    {
        // seaFraction / mountainFraction: part des sommets sous la mer / en montagne (on travaille sur le rang du bruit)
        // floors: etages de terrain. terraceRatio: part de chaque etage qui monte a l'etage du dessus (les etages s'emboitent
        // comme des courbes de niveau: plateaux au milieu des terres, montagnes au milieu du dernier etage)
        // triangles (plusieurs etages): grille, pour ne garder que les etages atteignables par les pentes (cf. LimitSlope)
        // holeScale > 0: trous a tous les etages (planete a niveaux). les etages viennent du bruit du relief sans mer, les trous
        // (hauteur 0, part seaFraction) d'un second bruit de cette frequence: un trou peut s'ouvrir au milieu de n'importe quel
        // etage, sans anneaux de pentes autour (les stamps du theme font tomber le plateau d'un bloc dans le vide)
        public static float[] VertexTargets(IReadOnlyList<Vector3> points, int seed, float scale, float seaFraction, float mountainFraction,
            int floors = 1, float terraceRatio = 0.5f, IReadOnlyList<int[]> triangles = null, float holeScale = 0f)
        {
            if (holeScale > 0f)
            {
                var levels = RankTargets(points, seed, scale, 0f, mountainFraction, floors, terraceRatio);
                for (int i = 0; i < levels.Length; i++)
                    levels[i] = Mathf.Max(1f, levels[i]);
                if (floors > 1 && triangles != null)
                    LimitSlope(levels, triangles);
                CarveHoles(levels, points, seed, holeScale, seaFraction);
                return levels;
            }

            var targets = RankTargets(points, seed, scale, seaFraction, mountainFraction, floors, terraceRatio);
            // un etage n'a pas de falaise: les pentes (et les montagnes) ne montent que d'un cran par arete
            if (floors > 1 && triangles != null)
                LimitSlope(targets, triangles);
            return targets;
        }

        // les sommets du rang le plus bas d'un second bruit deviennent des trous (cible 0)
        private static void CarveHoles(float[] targets, IReadOnlyList<Vector3> points, int seed, float scale, float fraction)
        {
            var offset = new Vector3(seed * 7.1f + 53.3f, seed * 13.7f + 17.9f, seed * 3.3f + 71.1f);
            var values = points.Select(p => Fractal(p.normalized * scale + offset)).ToArray();
            var order = Enumerable.Range(0, values.Length).OrderBy(i => values[i]).ToArray();
            int count = Mathf.RoundToInt(Mathf.Clamp01(fraction) * values.Length);
            for (int k = 0; k < count; k++)
                targets[order[k]] = 0f;
        }

        // le bruit met parfois le 3e etage juste a cote du 1er (petits continents): le WFC ne peut pas le faire (une piece monte
        // d'un etage au plus), il prenait alors le bord du plateau au mauvais etage et les etages du haut disparaissaient.
        // on garde la plus haute carte sous la carte voulue qui monte d'au plus 1 par arete (le plateau recule, sa pente reste)
        public static void LimitSlope(float[] targets, IReadOnlyList<int[]> triangles, float maxStep = 1f)
        {
            var edges = new HashSet<(int, int)>();
            foreach (var t in triangles)
                for (int e = 0; e < 3; e++)
                    edges.Add((Mathf.Min(t[e], t[(e + 1) % 3]), Mathf.Max(t[e], t[(e + 1) % 3])));
            var pairs = edges.ToArray();

            // le Mono de l'editeur calcule les additions de float en double: juste apres targets[a] = targets[b] + maxStep,
            // la comparaison targets[a] > targets[b] + maxStep pouvait rester vraie (arrondi) et la boucle tournait sans fin.
            // d'ou la marge, et un nombre de passes borne (une baisse ne se propage que sur quelques aretes: 3-4 passes en pratique)
            const float tolerance = 1e-4f;
            for (int pass = 0; pass < targets.Length; pass++)
            {
                bool changed = false;
                foreach (var (a, b) in pairs)
                {
                    if (targets[a] > targets[b] + maxStep + tolerance)
                    {
                        targets[a] = targets[b] + maxStep;
                        changed = true;
                    }
                    else if (targets[b] > targets[a] + maxStep + tolerance)
                    {
                        targets[b] = targets[a] + maxStep;
                        changed = true;
                    }
                }
                if (!changed)
                    break;
            }
        }

        // peinture a la main: les sommets peints prennent leur hauteur, les autres sont ramenes a au plus un etage de leurs voisins
        // (en montant comme en descendant): un plateau peint au milieu de la mer recoit ses pentes, le WFC peut le relier au reste.
        // les sommets sans cible (NaN) restent libres
        public static void ApplyPaint(float[] targets, IReadOnlyDictionary<int, float> painted, IReadOnlyList<int[]> triangles, float maxStep = 1f)
        {
            if (painted.Count == 0)
                return;
            foreach (var kv in painted)
                targets[kv.Key] = kv.Value;

            var edges = new HashSet<(int, int)>();
            foreach (var t in triangles)
                for (int e = 0; e < 3; e++)
                    edges.Add((Mathf.Min(t[e], t[(e + 1) % 3]), Mathf.Max(t[e], t[(e + 1) % 3])));
            var pairs = edges.ToArray();

            // meme garde-fou que LimitSlope (Mono calcule en double): marge + passes bornees. deux sommets peints incompatibles
            // (mer a cote du 3e etage) font osciller leurs voisins: la borne arrete la boucle, le WFC signalera le conflit
            const float tolerance = 1e-4f;
            bool Pull(int a, int b)
            {
                if (painted.ContainsKey(a) || float.IsNaN(targets[a]) || float.IsNaN(targets[b]))
                    return false;
                if (targets[a] > targets[b] + maxStep + tolerance)
                {
                    targets[a] = targets[b] + maxStep;
                    return true;
                }
                if (targets[a] < targets[b] - maxStep - tolerance)
                {
                    targets[a] = targets[b] - maxStep;
                    return true;
                }
                return false;
            }

            for (int pass = 0; pass < 64; pass++)
            {
                bool changed = false;
                foreach (var (a, b) in pairs)
                {
                    changed |= Pull(a, b);
                    changed |= Pull(b, a);
                }
                if (!changed)
                    break;
            }
        }

        private static float[] RankTargets(IReadOnlyList<Vector3> points, int seed, float scale, float seaFraction, float mountainFraction,
            int floors, float terraceRatio)
        {
            var offset = new Vector3(seed * 17.3f, seed * 5.1f, seed * 11.7f);
            var values = points.Select(p => Fractal(p.normalized * scale + offset)).ToArray();
            var order = Enumerable.Range(0, values.Length).OrderBy(i => values[i]).ToArray();

            // seuils de rang: mer | etage 1 | etage 2 | ... | etage floors | montagne
            float mountainLevel = 1f - mountainFraction;
            float land = Mathf.Max(0f, mountainLevel - seaFraction);
            var floorLevels = Enumerable.Range(2, Mathf.Max(0, floors - 1))
                .Select(k => mountainLevel - land * Mathf.Pow(terraceRatio, k - 1))
                .ToArray();

            var targets = new float[values.Length];
            for (int k = 0; k < order.Length; k++)
            {
                float rank = k / (float)Mathf.Max(1, order.Length - 1);
                float target = SmoothStep(seaFraction - 0.04f, seaFraction + 0.04f, rank)
                               + SmoothStep(mountainLevel - 0.03f, mountainLevel + 0.03f, rank);
                foreach (float level in floorLevels)
                    target += SmoothStep(level - 0.03f, level + 0.03f, rank);
                targets[order[k]] = target;
            }
            return targets;
        }

        // meme bruit que le relief (0..1, surtout entre 0.3 et 0.7), pour les motifs des themes (mer, lave)
        public static float Noise(Vector3 p) => Fractal(p);

        private static float SmoothStep(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        // bruit de valeur 3D lisse, 4 octaves
        private static float Fractal(Vector3 p)
        {
            float sum = 0f, amplitude = 1f, norm = 0f;
            for (int octave = 0; octave < 4; octave++)
            {
                sum += amplitude * Value(p);
                norm += amplitude;
                amplitude *= 0.55f;
                p *= 2.03f;
            }
            return sum / norm;
        }

        private static float Value(Vector3 p)
        {
            int x = Mathf.FloorToInt(p.x), y = Mathf.FloorToInt(p.y), z = Mathf.FloorToInt(p.z);
            float fx = Fade(p.x - x), fy = Fade(p.y - y), fz = Fade(p.z - z);
            float Lerp3(int dz) =>
                Mathf.Lerp(Mathf.Lerp(Hash(x, y, z + dz), Hash(x + 1, y, z + dz), fx),
                           Mathf.Lerp(Hash(x, y + 1, z + dz), Hash(x + 1, y + 1, z + dz), fx), fy);
            return Mathf.Lerp(Lerp3(0), Lerp3(1), fz);
        }

        private static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

        private static float Hash(int x, int y, int z)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + z * 1274126177;
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0x7fffffff) / (float)int.MaxValue;
            }
        }
    }
}
