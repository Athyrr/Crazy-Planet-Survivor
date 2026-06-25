using Unity.Entities;
using UnityEngine;

/// <summary>
/// Optional override for the enemy HP scaling tunables (see <see cref="EnemyScalingConfig"/>).
/// Place this on a baked GameObject (e.g. in the core entities subscene). If none exists in the
/// world, <see cref="EnemiesSpawnerSystem"/> falls back to <see cref="EnemyScalingConfig.Default"/>.
/// </summary>
public class EnemyScalingConfigAuthoring : MonoBehaviour
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
    [Tooltip("Enemy outgoing damage added per difficulty unit (linear). 0.3 = +30% per unit. Contact damage is frozen at spawn; enemy spell damage scales at cast.")]
    public float DamageGrowthPerUnit = 0.3f;

    [Tooltip("Hard cap on the enemy damage multiplier.")]
    public float MaxDamageMult = 10f;

    [Header("Player armor penetration")]
    [Tooltip("Flat player armor neutralized per difficulty unit. OFF by default (0): DamageGrowthPerUnit already makes flat armor decay in relevance, so enabling this too double-scales against armored players. Set > 0 only if you want armor to reach LITERALLY 0 mitigation late.")]
    public float ArmorPenPerUnit = 0f;

    [Tooltip("Hard cap on armor penetration (0 = uncapped, so armor keeps decaying all run).")]
    public float MaxArmorPen = 0f;

    [Tooltip("Minimum damage a hit deals after armor mitigation. 1 = armor never grants full immunity; 0 = weak hits can be fully blocked early.")]
    public float MinDamagePerHit = 1f;

    private class Baker : Baker<EnemyScalingConfigAuthoring>
    {
        public override void Bake(EnemyScalingConfigAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);
            AddComponent(entity, new EnemyScalingConfig
            {
                SecondsPerUnit = authoring.SecondsPerUnit,
                KillsPerUnit = authoring.KillsPerUnit,
                HealthGrowthPerUnit = authoring.HealthGrowthPerUnit,
                MaxHealthMult = authoring.MaxHealthMult,
                DamageGrowthPerUnit = authoring.DamageGrowthPerUnit,
                MaxDamageMult = authoring.MaxDamageMult,
                ArmorPenPerUnit = authoring.ArmorPenPerUnit,
                MaxArmorPen = authoring.MaxArmorPen,
                MinDamagePerHit = authoring.MinDamagePerHit
            });
        }
    }
}
