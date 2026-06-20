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
    public float KillsPerUnit = 150f;

    [Tooltip("Difficulty units (D) at which enemies reach MaxHealthMult (~end of run; D at ~10 min).")]
    public float ReferenceUnits = 16f;

    [Tooltip("CURVE SHAPE (main knob). <1 = harder early; 1 = linear ramp; >1 = easy early, spikes late.")]
    public float CurveExponent = 0.5f;

    [Tooltip("Late-game HP multiplier plateau (lower = easier endgame).")]
    public float MaxHealthMult = 2.5f;

    private class Baker : Baker<EnemyScalingConfigAuthoring>
    {
        public override void Bake(EnemyScalingConfigAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);
            AddComponent(entity, new EnemyScalingConfig
            {
                SecondsPerUnit = authoring.SecondsPerUnit,
                KillsPerUnit = authoring.KillsPerUnit,
                ReferenceUnits = authoring.ReferenceUnits,
                CurveExponent = authoring.CurveExponent,
                MaxHealthMult = authoring.MaxHealthMult
            });
        }
    }
}
