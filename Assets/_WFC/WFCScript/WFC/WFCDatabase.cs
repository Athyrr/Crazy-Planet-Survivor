using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WFCContent.HE;
using WFCContent.World;

namespace WFCContent.WFC
{
    [CreateAssetMenu(fileName = "SO_WFC_Database", menuName = "WFCDatabase", order = 1)]
    public class WFCDatabase : HESingletonSO<WFCDatabase>
    {
        // registre de TOUS les stamps de toutes les planetes (fenetre des regles, validation).
        // le WFC ne pioche que dans le theme de la planete
        [SerializeField] public List<WFCElementData> elements;
        // une planete = un theme (terre, lave...). l'importeur les ajoute dans l'ordre d'import
        [SerializeField] public List<WFCPlanetTheme> themes = new();
        // choisi a la main, l'importeur n'y touche pas (sinon le premier stamp importe decide du look de toutes les planetes)
        [SerializeField, Tooltip("Theme des planetes qui n'en ont pas. Vide = ancien comportement: les stamps ranges dans aucun theme")]
        public WFCPlanetTheme defaultTheme;

        public List<WFCElementData> Elements {
            get { return elements; }
            private set { elements = value; }
        }

        public IReadOnlyList<WFCPlanetTheme> Themes => themes;

        public WFCPlanetTheme DefaultTheme => defaultTheme;

        // planete sans theme: le registre sans les stamps d'un theme (les pieces vides restent, c'est la mer de toutes les planetes)
        public List<WFCElementData> UnthemedElements()
        {
            var themed = new HashSet<WFCElementData>((themes ?? new List<WFCPlanetTheme>())
                .Where(t => t != null && t.elements != null)
                .SelectMany(t => t.elements));
            return (elements ?? new List<WFCElementData>())
                .Where(e => e != null && (e.Prefab == null || !themed.Contains(e)))
                .ToList();
        }

        // id du theme (wfc_theme cote Blender), sans tenir compte de la casse. sinon nom de l'asset (Theme_Lava ou Lava)
        public WFCPlanetTheme FindTheme(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || themes == null)
                return null;
            id = id.Trim();

            bool Is(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
            return themes.FirstOrDefault(t => t != null && Is(t.id?.Trim(), id))
                   ?? themes.FirstOrDefault(t => t != null && (Is(t.name, id) || Is(t.name, "Theme_" + id)));
        }

#if UNITY_EDITOR
        [System.NonSerialized] private int? elementsHash;

        // meme logique que WFCElementData: ignore le OnValidate du chargement
        private void OnValidate()
        {
            var hash = new System.HashCode();
            foreach (var el in elements ?? new List<WFCElementData>())
                hash.Add(el);
            hash.Add(-1);
            foreach (var theme in themes ?? new List<WFCPlanetTheme>())
                hash.Add(theme);
            hash.Add(defaultTheme);
            int value = hash.ToHashCode();

            bool changed = elementsHash.HasValue && elementsHash.Value != value;
            elementsHash = value;
            if (changed)
                WFCElementData.NotifyRulesChanged();
        }
#endif
    }
}