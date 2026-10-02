using Unity.Entities;
using UnityEngine;
using _System.Settings;

/// <summary>
/// Optional override for the enemy HP scaling tunables (see <see cref="EnemyScalingConfig"/>).
/// Place this on a baked GameObject (e.g. in the core entities subscene). If none exists in the
/// world, <see cref="EnemiesSpawnerSystem"/> falls back to <see cref="EnemyScalingConfig.Default"/>.
/// </summary>
public class EnemyScalingConfigAuthoring : MonoBehaviour
{
    public CpDifficultyScalingSettings Settings;

    private class Baker : Baker<EnemyScalingConfigAuthoring>
    {
        public override void Bake(EnemyScalingConfigAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);
            var s = authoring.Settings;
            if (s != null)
                DependsOn(s);

            AddComponent(entity, new EnemyScalingConfig
            {
                SecondsPerUnit = s != null ? s.SecondsPerUnit : 60f,
                KillsPerUnit = s != null ? s.KillsPerUnit : 100f,
                HealthGrowthPerUnit = s != null ? s.HealthGrowthPerUnit : 0.5f,
                MaxHealthMult = s != null ? s.MaxHealthMult : 20f,
                DamageGrowthPerUnit = s != null ? s.DamageGrowthPerUnit : 0.3f,
                MaxDamageMult = s != null ? s.MaxDamageMult : 10f,
                ArmorPenPerUnit = s != null ? s.ArmorPenPerUnit : 0f,
                MaxArmorPen = s != null ? s.MaxArmorPen : 0f,
                MinDamagePerHit = s != null ? s.MinDamagePerHit : 1f,
            });
        }
    }
}
