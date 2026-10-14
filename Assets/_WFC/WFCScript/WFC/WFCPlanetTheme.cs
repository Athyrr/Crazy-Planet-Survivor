using System.Collections.Generic;
using UnityEngine;
using WFCContent.World;

namespace WFCContent.WFC
{
    // un theme = une planete (terre, lave...): les stamps dans lesquels pioche le WFC + le look de la mer (niveau 0)
    // la WFCDatabase garde le registre de tous les stamps, le theme n'en garde qu'une partie
    [CreateAssetMenu(fileName = "Theme_New", menuName = "WFC/Planet Theme", order = 2)]
    public class WFCPlanetTheme : ScriptableObject
    {
        [SerializeField, Tooltip("Identifiant = valeur de wfc_theme cote Blender, ex \"Earth\", \"Lava\". Sert a WFCDatabase.FindTheme")]
        public string id;
        [SerializeField, Tooltip("Stamps de cette planete, le WFC ne pioche que dedans. Doit contenir une piece vide (0,0,0) pour la mer")]
        public List<WFCElementData> elements = new();

        [Header("Mer (niveau 0)")]
        [SerializeField] public Color seaColor = new Color(0.70f, 0.80f, 0.78f);
        [SerializeField, Tooltip("Deuxieme couleur de la mer, melangee a la premiere par un bruit")]
        public Color seaColorVariation = new Color(0.70f, 0.80f, 0.78f);
        [SerializeField, Range(0f, 1f), Tooltip("0 = mer unie, 1 = motif bien marque (coulees de lave, courants...)")]
        public float seaVariation = 0f;
        [SerializeField, Range(0.1f, 20f), Tooltip("Frequence du motif: plus grand = taches plus petites")]
        public float seaVariationScale = 3f;
        [SerializeField, Range(0f, 1f), Tooltip("0 = eau mate, 1 = lave qui brille. Ecrit dans l'alpha des vertex colors, lu par SG_VertexColor")]
        public float seaEmission = 0f;
        [SerializeField, Tooltip("Vide = garde le material de la planete")]
        public Material seaMaterial;
        [SerializeField, Range(0f, 5f), Tooltip("Profondeur du niveau 0 sous la surface, en hauteurs de cage: 0 = mer au ras du sol, " +
                                                "2-3 = gouffres sans fond (planete desert). Donnee par les stamps (wfc_sea_depth), ecrite par l'importeur")]
        public float seaDepth = 0f;

        [Header("Planete creuse")]
        [SerializeField, Range(0f, 0.9f), Tooltip("0 = planete pleine. Sinon la planete est une coquille: la sphere du niveau 0 devient le noyau, " +
                                                  "de ce rayon (fraction du rayon), vu par les trous. Donne par les stamps (wfc_core), ecrit par l'importeur")]
        public float coreRadius = 0f;
        [SerializeField, Min(0f), Tooltip("Lumiere au centre (couleur de la mer): eclaire l'interieur de la croute. 0 = pas de lumiere")]
        public float coreLightIntensity = 0f;

        [Header("Relief")]
        [SerializeField, Tooltip("Le relief ci-dessous remplace celui de la planete")]
        public bool overrideRelief = false;
        [SerializeField, Range(0f, 10f), Tooltip("0 = WFC libre (terres tres morcelees). Plus fort = le WFC suit la carte de relief")]
        public float reliefStrength = 6f;
        [SerializeField, Range(0.3f, 4f), Tooltip("Frequence du relief: plus petit = continents plus grands")]
        public float reliefScale = 1.6f;
        [SerializeField, Range(0f, 1f), Tooltip("Part des sommets sous la mer")]
        public float seaFraction = 0.55f;
        [SerializeField, Range(0f, 0.5f), Tooltip("Part des sommets en montagne")]
        public float mountainFraction = 0.08f;
        [SerializeField, Range(1, GoldbergPolyhedron.MaxFloors), Tooltip(GoldbergPolyhedron.FloorsTooltip)]
        public int floors = 1;
        [SerializeField, Range(0.1f, 0.9f), Tooltip(GoldbergPolyhedron.TerraceRatioTooltip)]
        public float terraceRatio = 0.5f;
        [SerializeField, Tooltip("Planete a niveaux (stamps Alien): les trous (niveau 0, part = seaFraction) s'ouvrent a n'importe quel etage, " +
                                 "sans anneaux de pentes autour. Il faut des stamps de bord de trou pour chaque etage")]
        public bool holesAnyLevel = false;
        [SerializeField, Range(0.3f, 8f), Tooltip("Frequence des trous (trous a tous les etages): plus grand = trous plus petits et plus nombreux")]
        public float holeScale = 2.6f;

        [Header("Etages")]
        [SerializeField, Range(0.05f, 1f), Tooltip("Hauteur d'un etage en fraction de la cage (layerHeight). Donnee par les stamps " +
                                                   "(FLOOR_STEP / H du generateur Blender), ecrite par l'importeur: a ne pas changer a la main")]
        public float floorHeight = GoldbergPolyhedron.DefaultFloorHeight;

        // couleur de la mer en un point de la planete: vertex color lineaire, alpha = 1 - emission
        public Color SeaColorAt(Vector3 direction, int seed)
        {
            // decalage different de celui du relief, sinon le motif suit les cotes.
            // graine ramenee sous 1000: un grand decalage en float perd la partie decimale du bruit (motif en escalier)
            int s = seed % 1000;
            var offset = new Vector3(s * 3.7f + 100f, s * 13.1f, s * 7.9f);
            float n = ReliefMap.Noise(direction.normalized * seaVariationScale + offset);
            // le bruit de valeur reste surtout entre 0.3 et 0.7
            float t = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 0.7f, n));
            var color = Color.Lerp(seaColor, seaColorVariation, t * seaVariation).linear;
            color.a = 1f - seaEmission;
            return color;
        }

#if UNITY_EDITOR
        [System.NonSerialized] private int? themeHash;

        // meme logique que WFCElementData: ignore le OnValidate du chargement
        private void OnValidate()
        {
            var hash = new System.HashCode();
            hash.Add(id);
            foreach (var el in elements ?? new List<WFCElementData>())
                hash.Add(el);
            hash.Add(seaColor);
            hash.Add(seaColorVariation);
            hash.Add(seaVariation);
            hash.Add(seaVariationScale);
            hash.Add(seaEmission);
            hash.Add(seaMaterial);
            hash.Add(seaDepth);
            hash.Add(coreRadius);
            hash.Add(coreLightIntensity);
            hash.Add(overrideRelief);
            hash.Add(reliefStrength);
            hash.Add(reliefScale);
            hash.Add(seaFraction);
            hash.Add(mountainFraction);
            hash.Add(floors);
            hash.Add(terraceRatio);
            hash.Add(holesAnyLevel);
            hash.Add(holeScale);
            hash.Add(floorHeight);
            int value = hash.ToHashCode();

            bool changed = themeHash.HasValue && themeHash.Value != value;
            themeHash = value;
            if (changed)
                WFCElementData.NotifyRulesChanged();
        }
#endif
    }
}
