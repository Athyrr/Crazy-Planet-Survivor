
using System.Collections.Generic;
using UnityEngine;
using WFCContent.World;

public static class FreeFormDeformer
{
    private const float DegenerateLatticeTolerance = 1e-6f;

    public static List<(Vector3, Vector3)> DebugValue = new ();
    public static List<(Vector3, Vector3)> DebugGrid = new ();
    // debug (deplacement des sommets, grille de la cage): ~1000 segments par mesh, des dizaines de Mo sur une planete.
    // seulement quand on l'affiche (GoldbergPolyhedron.enableLatticeDebug), jamais en jeu
    public static bool CollectDebug;

    // sphereCenter: si fourni, la position horizontale est ramenee sur la sphere (courbure de la planete), la hauteur est conservee
    // heightOffset: decalage vertical en hauteurs de cage (stamps empiles sur les etages). ajoute a la coordonnee de hauteur,
    // il suit exactement la meme echelle que le relief du stamp: le haut d'une pente tombe pile sur le stamp de l'etage du dessus
    public static void Init(List<Vector3> container, List<Vector3> target, Mesh targetMesh,
        Transform meshTransform, Vector3? sphereCenter = null, float heightOffset = 0f)
    {
        // re order par rapport a vertice que j'ai placer car j'ai trop la flemme de faire un casse tete a 1h du math. et meme si j'etais payer et qu'il etait seulement 11h je ne pense pas le faire ^^
        if (container.Count != 6 || target.Count != 6)
            return;

        if (!TryCreateCoordinateSystem(target, out LatticeCoordinateSystem coordinateSystem))
            return;
        
        List<Vector3> orderedContainer = OrderContainerPairs(container, target);
        
        var verts = targetMesh.vertices;
        var localToWorld = meshTransform.localToWorldMatrix;
        var worldToLocal = meshTransform.worldToLocalMatrix;
        for (int i = 0; i < verts.Length; i++)
        {
            Vector3 worldStart = localToWorld.MultiplyPoint3x4(verts[i]);
            Vector3 coordinates = coordinateSystem.GetCoordinates(worldStart);
            float s = coordinates.x;
            float t = coordinates.y;
            float u = coordinates.z + heightOffset;

            Vector3 worldEnd = sphereCenter.HasValue
                ? FollowSphere(orderedContainer, s, t, u, sphereCenter.Value)
                : Evaluate(orderedContainer, s, t, u);

            if (CollectDebug)
                DebugValue.Add((worldStart, worldEnd));
            verts[i] = worldToLocal.MultiplyPoint3x4(worldEnd);
        }

        targetMesh.vertices = verts;
        targetMesh.RecalculateNormals();
        targetMesh.RecalculateBounds();

        if (CollectDebug)
            BuildDebugGrid(orderedContainer, 8);
    }

    private static List<Vector3> OrderContainerPairs(List<Vector3> container, List<Vector3> target)
    {
        int bestA = 0;
        int bestB = 1;
        int bestC = 2;
        float bestCost = float.PositiveInfinity;

        for (int a = 0; a < 3; a++)
        {
            for (int b = 0; b < 3; b++)
            {
                if (b == a)
                    continue;

                int c = 3 - a - b;
                float cost =
                    (target[0] - container[a]).sqrMagnitude +
                    (target[1] - container[b]).sqrMagnitude +
                    (target[2] - container[c]).sqrMagnitude +
                    (target[3] - container[a + 3]).sqrMagnitude +
                    (target[4] - container[b + 3]).sqrMagnitude +
                    (target[5] - container[c + 3]).sqrMagnitude;

                if (cost >= bestCost)
                    continue;

                bestCost = cost;
                bestA = a;
                bestB = b;
                bestC = c;
            }
        }

        return new List<Vector3>(6)
        {
            container[bestA],
            container[bestB],
            container[bestC],
            container[bestA + 3],
            container[bestB + 3],
            container[bestC + 3]
        };
    }

    public static void ClearDebug()
    {
        // nouvelles listes: Clear garderait la capacite (la memoire du plus gros Generate)
        DebugValue = new();
        DebugGrid = new();
    }

    // la cage est un prisme plat: sans ca, chaque stamp est une facette plane et la planete parait polygonale.
    // un point d'une arete ne depend que des deux sommets de l'arete: les stamps voisins restent raccordes
    private static Vector3 FollowSphere(List<Vector3> lattice, float s, float t, float u, Vector3 center)
    {
        Vector3 bottom = Evaluate(lattice, s, t, 0f);
        Vector3 top = Evaluate(lattice, s, t, 1f);
        float radius = ((lattice[0] - center).magnitude + (lattice[1] - center).magnitude + (lattice[2] - center).magnitude) / 3f;
        return center + (bottom - center).normalized * (radius + u * (top - bottom).magnitude);
    }

    private static Vector3 Evaluate(List<Vector3> lattice, float s, float t, float u)
    {
        return
            lattice[0] * (1f - s - t) * (1f - u) +
            lattice[1] * s * (1f - u) +
            lattice[2] * t * (1f - u) +
            lattice[3] * (1f - s - t) * u +
            lattice[4] * s * u +
            lattice[5] * t * u;
    }

    private static void BuildDebugGrid(List<Vector3> lattice, int resolution)
    {
        float step = 1f / resolution;

        for (int u = 0; u <= resolution; u++)
        {
            for (int s = 0; s <= resolution; s++)
            {
                for (int t = 0; t <= resolution - s; t++)
                {
                    Vector3 point = Evaluate(lattice, s * step, t * step, u * step);

                    if (s < resolution - t)
                        DebugGrid.Add((point, Evaluate(lattice, (s + 1) * step, t * step, u * step)));
                    if (t < resolution - s)
                        DebugGrid.Add((point, Evaluate(lattice, s * step, (t + 1) * step, u * step)));
                    if (u < resolution)
                        DebugGrid.Add((point, Evaluate(lattice, s * step, t * step, (u + 1) * step)));
                }
            }
        }
    }

    // hard fix car j'ai pas mis dans le bonne ordre.
    private static bool TryCreateCoordinateSystem(
        List<Vector3> lattice,
        out LatticeCoordinateSystem coordinateSystem)
    {
        Vector3 axisS = lattice[1] - lattice[0];
        Vector3 axisT = lattice[2] - lattice[0];
        Vector3 axisU = lattice[3] - lattice[0];
        float scale = axisS.magnitude * axisT.magnitude * axisU.magnitude;
        float determinant = Vector3.Dot(axisS, Vector3.Cross(axisT, axisU));

        if (scale <= Mathf.Epsilon ||
            Mathf.Abs(determinant) <= scale * DegenerateLatticeTolerance)
        {
            coordinateSystem = default;
            return false;
        }

        coordinateSystem = new LatticeCoordinateSystem(
            lattice[0],
            Vector3.Cross(axisT, axisU),
            Vector3.Cross(axisU, axisS),
            Vector3.Cross(axisS, axisT),
            1f / determinant);
        return true;
    }

    private readonly struct LatticeCoordinateSystem
    {
        private readonly Vector3 origin;
        private readonly Vector3 crossTU;
        private readonly Vector3 crossUS;
        private readonly Vector3 crossST;
        private readonly float inverseDeterminant;

        public LatticeCoordinateSystem(
            Vector3 origin,
            Vector3 crossTU,
            Vector3 crossUS,
            Vector3 crossST,
            float inverseDeterminant)
        {
            this.origin = origin;
            this.crossTU = crossTU;
            this.crossUS = crossUS;
            this.crossST = crossST;
            this.inverseDeterminant = inverseDeterminant;
        }

        public Vector3 GetCoordinates(Vector3 point)
        {
            Vector3 position = point - origin;
            return new Vector3(
                Vector3.Dot(position, crossTU) * inverseDeterminant,
                Vector3.Dot(position, crossUS) * inverseDeterminant,
                Vector3.Dot(position, crossST) * inverseDeterminant);
        }
    }
}
