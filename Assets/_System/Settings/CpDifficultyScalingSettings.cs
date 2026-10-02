using UnityEngine;

namespace _System.Settings
{
    /// <summary>Tunables for the run difficulty scaling — see EnemyScalingConfig for the full formula
    /// documentation. Renamed from the old raw-field EnemyScalingConfigAuthoring to match this project's
    /// singleton-settings convention; the runtime EnemyScalingConfig component and its consumers
    /// (EnemiesSpawnerSystem, HealthSystem, SpellCastingSystem) are unchanged.</summary>
    [CreateAssetMenu(fileName = "CpDifficultyScalingSettings", menuName = "CPSettings/DifficultyScalingSettings")]
    public class CpDifficultyScalingSettings : CpSettings<CpDifficultyScalingSettings>
    {
        [Tooltip("Run seconds per difficulty unit. Lower = faster ramp from elapsed time.")]
        public float SecondsPerUnit = 60f;

        [Tooltip("Enemies killed per difficulty unit. Lower = faster ramp from kills.")]
        public float KillsPerUnit = 100f;

        [Tooltip("HP multiplier added per difficulty unit (linear). 0.5 = +50% of base HP per unit.")]
        public float HealthGrowthPerUnit = 0.5f;

        [Tooltip("Hard cap on the spawned-enemy HP multiplier.")]
        public float MaxHealthMult = 20f;

        [Header("Enemy damage scaling")]
        [Tooltip("Enemy outgoing damage added per difficulty unit (linear). 0.3 = +30% per unit.")]
        public float DamageGrowthPerUnit = 0.3f;

        [Tooltip("Hard cap on the enemy damage multiplier.")]
        public float MaxDamageMult = 10f;

        [Header("Player armor penetration")]
        [Tooltip("Flat player armor neutralized per difficulty unit. OFF by default (0).")]
        public float ArmorPenPerUnit = 0f;

        [Tooltip("Hard cap on armor penetration (0 = uncapped).")]
        public float MaxArmorPen = 0f;

        [Tooltip("Minimum damage a hit deals after armor mitigation.")]
        public float MinDamagePerHit = 1f;
    }
}
