using System;
using System.Collections.Generic;
using UnityEngine;
using WFCContent.HE;

namespace WFCContent.WFC
{
    [Serializable]
    public struct EligibilityData
    {
        public Vector3 position;
        public List<WFCElementData> excludedElements;
        public List<WFCElementData> includedElements;
    }

    // ce qui traverse une arete du triangle, deux voisins doivent avoir la meme chose sur l'arete partagee
    public enum WFCEdge
    {
        None = 0,
        River = 1,
        // pont au-dessus du vide: le tablier passe au milieu de l'arete (planete a gouffres). planete a niveaux: pont de l'etage 1
        Bridge = 2,
        // planete a niveaux: falaise entre deux etages (sinon None = escalier taille, les deux voisins ont le meme passage)
        Cliff = 3,
        // planete a niveaux: pont au-dessus du vide a l'etage 2, 3...
        Bridge2 = 4,
        Bridge3 = 5,
        Bridge4 = 6,
        Bridge5 = 7
    }

    [CreateAssetMenu(fileName = "SO_WFC_Element", menuName = "WFCElementData", order = 1)]
    public class WFCElementData : ScriptableObject
    {
        [SerializeField, Tooltip("Vide = piece vide (rien n'est instancie)")] public GameObject Prefab;
        [SerializeField] public EligibilityData Data = new();

        // hauteur aux 3 sommets du triangle (marching triangles), deux voisins doivent avoir la meme hauteur aux sommets partages
        [Header("Coins")]
        [SerializeField, Tooltip("Pointe du mesh (au centre de la cellule quand la piece n'est pas tournee)")]
        public int heightApex;
        [SerializeField] public int heightCornerA;
        [SerializeField] public int heightCornerB;
        [SerializeField, Tooltip("La pointe peut aussi aller sur les coins de la cellule")]
        public bool allowRotation = true;
        [SerializeField, Min(0f), Tooltip("Probabilite relative d'etre choisie")]
        public float weight = 1f;

        // arete i = entre le sommet i et le sommet i+1 (0 = pointe, 1 = A, 2 = B)
        [Header("Aretes")]
        [SerializeField] public WFCEdge edgeApexA;
        [SerializeField, Tooltip("Bord de la cellule")] public WFCEdge edgeAB;
        [SerializeField] public WFCEdge edgeBApex;

        // hauteurs = etages (0 mer, 1 terre, 2, 3... plateaux). la piece est modelisee au niveau de la mer, sur une planete a
        // plusieurs etages elle est aussi posee plus haut (hauteurs + decalage, stamp monte d'autant dans sa cage FFD)
        [Header("Etages")]
        [SerializeField, Tooltip("Montagne: le coin le plus haut est un pic (pas un plateau). Posee sur le dernier etage de la planete")]
        public bool peak;
        [SerializeField, Tooltip("Peut aussi etre posee sur les etages du dessus. Une piece avec de la mer (coin a 0) reste au niveau de la mer")]
        public bool stackable = true;

        public int[] CornerHeights => new[] { heightApex, heightCornerA, heightCornerB };
        public WFCEdge[] Edges => new[] { edgeApexA, edgeAB, edgeBApex };

        // decalages possibles (en etages) sur une planete a `floors` etages: hauteurs finales = coins + decalage.
        // mer 0, etages 1..floors, pic d'une montagne floors + 1 (planete a 1 etage: 0 mer, 1 terre, 2 montagne)
        public IEnumerable<int> Levels(int floors)
        {
            int low = Mathf.Min(heightApex, Mathf.Min(heightCornerA, heightCornerB));
            int high = Mathf.Max(heightApex, Mathf.Max(heightCornerA, heightCornerB));
            bool canRise = low > 0 && stackable;
            if (peak)
            {
                int level = floors + 1 - high;
                if (level == 0 || (level > 0 && canRise))
                    yield return level;
                yield break;
            }

            int top = canRise ? floors - high : Mathf.Min(0, floors - high);
            for (int level = 0; level <= top; level++)
                yield return level;
        }

        // exclu = interdit comme voisin
        // inclus = whitelist, si la liste est vide tout est accepte
        public bool Accepts(WFCElementData other)
        {
            if (Data.excludedElements != null && Data.excludedElements.Contains(other))
                return false;

            if (!HasWhitelist())
                return true;

            return Data.includedElements.Contains(other);
        }

        public bool HasWhitelist()
        {
            if (Data.includedElements == null)
                return false;
            foreach (var el in Data.includedElements)
                if (el != null)
                    return true;
            return false;
        }

        // les deux pieces doivent s'accepter mutuellement
        public static bool AreCompatible(WFCElementData a, WFCElementData b) => a.Accepts(b) && b.Accepts(a);

#if UNITY_EDITOR
        // ecoute par la live preview (editor only)
        public static event Action RulesChanged;

        public static void NotifyRulesChanged() => RulesChanged?.Invoke();

        // OnValidate est aussi appele au chargement / recompile, on ne notifie que si les regles ont vraiment bouge
        [NonSerialized] private int? rulesHash;

        private void OnValidate()
        {
            int hash = ComputeRulesHash();
            bool changed = rulesHash.HasValue && rulesHash.Value != hash;
            rulesHash = hash;
            if (changed)
                NotifyRulesChanged();
        }

        private int ComputeRulesHash()
        {
            var hash = new HashCode();
            hash.Add(Prefab);
            hash.Add(heightApex);
            hash.Add(heightCornerA);
            hash.Add(heightCornerB);
            hash.Add(edgeApexA);
            hash.Add(edgeAB);
            hash.Add(edgeBApex);
            hash.Add(allowRotation);
            hash.Add(weight);
            hash.Add(peak);
            hash.Add(stackable);
            foreach (var el in Data.excludedElements ?? new List<WFCElementData>())
                hash.Add(el);
            hash.Add(-1);
            foreach (var el in Data.includedElements ?? new List<WFCElementData>())
                hash.Add(el);
            return hash.ToHashCode();
        }
#endif
    }
}
