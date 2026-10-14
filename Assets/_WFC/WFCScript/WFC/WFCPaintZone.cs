using System;
using System.Collections.Generic;
using UnityEngine;

namespace WFCContent.WFC
{
    // zone peinte sur la planete (foret, village, desert...): change les chances des pieces dans les slots peints.
    // reutilisable d'une planete a l'autre (asset), editee dans Tools/WFC/Planet Painter
    [CreateAssetMenu(fileName = "Zone_New", menuName = "WFC/Paint Zone", order = 3)]
    public class WFCPaintZone : ScriptableObject
    {
        public enum RuleMode
        {
            Favor = 0,  // poids x force
            Avoid = 1,  // poids / force
            Forbid = 2  // jamais (regle dure)
        }

        [Serializable]
        public class Rule
        {
            public WFCElementData element;
            public RuleMode mode;
        }

        [SerializeField, Tooltip("Couleur de la zone dans la vue de peinture")]
        public Color color = new Color(0.30f, 0.80f, 0.40f);
        [SerializeField, Range(1.5f, 100f), Tooltip("Favoriser = poids x force, Eviter = poids / force")]
        public float strength = 12f;
        [SerializeField, Tooltip("Les pieces qui ne sont pas favorisees sont evitees (poids / force^2)")]
        public bool onlyFavored;
        [SerializeField] public List<Rule> rules = new();

        public Rule Find(WFCElementData element)
        {
            if (rules == null)
                return null;
            foreach (var rule in rules)
                if (rule != null && rule.element == element)
                    return rule;
            return null;
        }

        // multiplicateur du poids d'une piece dans la zone (0 = interdite)
        public float FactorFor(WFCElementData element)
        {
            var rule = Find(element);
            if (rule != null)
                return rule.mode switch
                {
                    RuleMode.Favor => strength,
                    RuleMode.Avoid => 1f / strength,
                    _ => 0f
                };
            if (onlyFavored && rules != null && rules.Exists(r => r != null && r.element != null && r.mode == RuleMode.Favor))
                return 1f / (strength * strength);
            return 1f;
        }

        public void SetRule(WFCElementData element, RuleMode? mode)
        {
            rules ??= new List<Rule>();
            rules.RemoveAll(r => r == null || r.element == null || r.element == element);
            if (mode.HasValue)
                rules.Add(new Rule { element = element, mode = mode.Value });
        }
    }
}
