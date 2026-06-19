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
                MaxHealthMult = authoring.MaxHealthMult
            });
        }
    }
}
