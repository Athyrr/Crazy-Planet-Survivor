using UnityEngine;

namespace _System.Settings
{
    /// <summary>
    /// Central configuration for the upgrade rarity system: per-tier drop weights, crystal
    /// materials, labels and text colors, plus the Luck curve and the spell-drop cadence.
    /// Single source of truth for designers. Gameplay-relevant numbers (weights, luck curve,
    /// spell interval) are baked into the <c>RaritySettings</c> ECS singleton for the Burst
    /// selection job; visuals are read directly here by the upgrade card view.
    /// </summary>
    [CreateAssetMenu(fileName = "CpRaritySettings", menuName = "CPSettings/RaritySettings")]
    public class CpRaritySettings : CpSettings<CpRaritySettings>
    {
        /// <summary>Shader property (on UberFXSG) driven by a tier's crystal density.</summary>
        public const string CrystalDensityShaderProperty = "VertexColorNoiseOpacity";

        /// <summary>
        /// Toggle (on UberFXSG) that must be enabled for <see cref="CrystalDensityShaderProperty"/>
        /// to take effect. Set to 1 alongside the density.
        /// </summary>
        public const string CrystalDensityEnableShaderProperty = "EnableVertexColorNoiseOpacity";

        [System.Serializable]
        public struct RarityTierSettings
        {
            public ERarity Rarity;

            [Tooltip("Label shown on the upgrade card (e.g. \"RARE\").")]
            public string Label;

            [Tooltip("Color of the rarity label text.")]
            public Color TextColor;

            [Tooltip("Base drop weight before Luck. Higher = more frequent.")]
            [Min(0f)] public float BaseWeight;

            [Tooltip("Material applied to the upgrade crystal for this rarity.")]
            public Material CrystalMaterial;

            [Tooltip("Drives the crystal shader's '" + CrystalDensityShaderProperty +
                     "' (higher = more visible noise/crystal).")]
            public float CrystalDensity;
        }

        [Header("Tiers (one entry per ERarity)")]
        [SerializeField]
        private RarityTierSettings[] _tiers =
        {
            new RarityTierSettings { Rarity = ERarity.Common,    Label = "COMMON",    TextColor = new Color(0.80f, 0.80f, 0.80f), BaseWeight = 60f, CrystalDensity = 0.15f },
            new RarityTierSettings { Rarity = ERarity.Uncommon,  Label = "UNCOMMON",  TextColor = new Color(0.40f, 0.85f, 0.35f), BaseWeight = 25f, CrystalDensity = 0.35f },
            new RarityTierSettings { Rarity = ERarity.Rare,      Label = "RARE",      TextColor = new Color(0.30f, 0.55f, 1.00f), BaseWeight = 10f, CrystalDensity = 0.55f },
            new RarityTierSettings { Rarity = ERarity.Epic,      Label = "EPIC",      TextColor = new Color(0.70f, 0.35f, 1.00f), BaseWeight = 4f,  CrystalDensity = 0.78f },
            new RarityTierSettings { Rarity = ERarity.Legendary, Label = "LEGENDARY", TextColor = new Color(1.00f, 0.62f, 0.10f), BaseWeight = 1f,  CrystalDensity = 1.00f },
        };

        [Header("Spell crystal (no rarity)")]
        [Tooltip("Material applied to the crystal for spell unlock / spell upgrade cards.")]
        [SerializeField] private Material _spellCrystalMaterial;
        [Tooltip("Crystal density used for spell cards.")]
        [SerializeField] private float _spellCrystalDensity = 0.5f;
        [Tooltip("Text color used for the rarity label of spell cards.")]
        [SerializeField] private Color _spellTextColor = new Color(1f, 0.92f, 0.016f);
        [SerializeField] private string _spellLabel = "SPELL";

        [Header("Luck")]
        [Tooltip("Luck value mapped to the curve's 1.0 (X axis upper bound) when sampling.")]
        [Min(0.0001f)] [SerializeField] private float _maxLuck = 100f;
        [Tooltip("Maps normalized Luck (0..1) to a rare-weight multiplier. Final tier weight = " +
                 "BaseWeight * pow(luckFactor, tierIndex), so higher tiers benefit more from Luck.")]
        [SerializeField] private AnimationCurve _luckToRareWeight = AnimationCurve.Linear(0f, 1f, 1f, 3f);

        [Header("Selection cadence")]
        [Tooltip("Every Nth level offers spells instead of stat upgrades.")]
        [Min(1)] [SerializeField] private int _spellDropLevelInterval = 4;

        #region Tier access (visuals — read by the upgrade card view)

        /// <summary>Returns the settings for a rarity tier (falls back to Common if missing).</summary>
        public static RarityTierSettings GetTier(ERarity rarity)
        {
            var tiers = I._tiers;
            if (tiers != null)
            {
                for (int i = 0; i < tiers.Length; i++)
                    if (tiers[i].Rarity == rarity)
                        return tiers[i];

                int idx = (int)rarity;
                if (idx >= 0 && idx < tiers.Length)
                    return tiers[idx];
            }
            return default;
        }

        public static string GetLabel(ERarity rarity) => GetTier(rarity).Label;
        public static Color GetTextColor(ERarity rarity) => GetTier(rarity).TextColor;
        public static Material GetCrystalMaterial(ERarity rarity) => GetTier(rarity).CrystalMaterial;
        public static float GetCrystalDensity(ERarity rarity) => GetTier(rarity).CrystalDensity;

        public static Material SpellCrystalMaterial => I._spellCrystalMaterial;
        public static float SpellCrystalDensity => I._spellCrystalDensity;
        public static Color SpellTextColor => I._spellTextColor;
        public static string SpellLabel => I._spellLabel;

        #endregion

        #region Baker access (gameplay numbers — baked into the RaritySettings singleton)

        public int SpellDropLevelInterval => _spellDropLevelInterval;
        public float MaxLuck => Mathf.Max(0.0001f, _maxLuck);

        /// <summary>Base drop weight for a tier (used by the baker to fill the weights array).</summary>
        public float GetBaseWeight(ERarity rarity)
        {
            if (_tiers != null)
            {
                for (int i = 0; i < _tiers.Length; i++)
                    if (_tiers[i].Rarity == rarity)
                        return _tiers[i].BaseWeight;
            }
            return 0f;
        }

        /// <summary>Samples the luck→rare-weight multiplier at a normalized luck value (0..1).</summary>
        public float SampleLuckFactor(float luck01) => _luckToRareWeight.Evaluate(Mathf.Clamp01(luck01));

        #endregion
    }
}
