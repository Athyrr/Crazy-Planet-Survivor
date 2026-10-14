using System;
using System.Collections.Generic;

namespace WFCContent.WFC
{
    // piece posee sur un slot: element + rotation (sommet du slot qui recoit la pointe) + decalage en etages
    [Serializable]
    public struct WFCSlotKey : IEquatable<WFCSlotKey>
    {
        public WFCElementData element;
        public int rotation;
        public int level;

        public WFCSlotKey(WFCElementData element, int rotation, int level)
        {
            this.element = element;
            this.rotation = rotation;
            this.level = level;
        }

        // hauteur aux sommets [o, a, b] du slot, decalage compris (meme rotation que WaveFunctionCollapseManager.BuildVariants)
        public int[] CornerHeights()
        {
            var heights = new int[3];
            if (element == null)
                return heights;
            var source = element.CornerHeights;
            for (int i = 0; i < 3; i++)
                heights[(i + rotation) % 3] = source[i] + level;
            return heights;
        }

        public bool Equals(WFCSlotKey other) => element == other.element && rotation == other.rotation && level == other.level;
        public override bool Equals(object obj) => obj is WFCSlotKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(element, rotation, level);
        public override string ToString() => element != null ? $"{element.name} r{rotation} +{level}" : "(vide)";
    }

    // contraintes posees a la main (peinture de la planete) ou par une re-resolution locale. chaque tableau est optionnel (null = rien)
    // dur: le domaine de depart du slot est filtre (VertexHeights, Allowed, Weights a 0, Fixed). si plus rien ne passe, le slot
    // retombe sur ses pieces normales et finit dans WFCSolveResult.Relaxed
    // souple: Weights multiplie le poids des pieces, TargetWeights donne plus ou moins d'importance a la cible de relief d'un sommet
    public class WFCConstraints
    {
        // par sommet de la grille: hauteur imposee, -1 = libre
        public int[] VertexHeights;
        // par sommet: poids de la cible de relief (0 = pas de cible). null = 1 partout
        public float[] TargetWeights;
        // par slot: pieces autorisees (null = toutes)
        public HashSet<WFCElementData>[] Allowed;
        // par slot: rotation imposee aux pieces autorisees (-1 = libre)
        public int[] AllowedRotation;
        // par slot: multiplicateur de poids d'une piece (0 = interdite, 1 = normal). null = 1 partout
        public Func<WFCElementData, float>[] Weights;
        // par slot: resolu avant les autres (peinture souple: le choix de la main passe avant ce que les voisins imposeraient)
        public bool[] Priority;
        // par slot: piece figee (gel, ou slot hors de la zone re-resolue). element null = libre.
        // un slot fige ne bouge plus et ne recoit pas la propagation: ses voisins s'adaptent a lui
        public WFCSlotKey[] Fixed;
    }
}
