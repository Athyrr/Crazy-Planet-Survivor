using UnityEngine;
using _System.Settings;

[CreateAssetMenu(fileName = "CpEmitterSettings", menuName = "CPSettings/EmitterSettings")]
public class CpEmitterSettings : CpSettings<CpEmitterSettings>
{
    [Tooltip("Explosion damage = root FinalDamage × this (an emitter entry DamageRatio > 0 overrides it).")]
    public float ExplosionDamageRatio = 1f;
    [Tooltip("Off: explosions never life-steal (CoreStats.ExplosionCanLifeSteal can re-enable it).")]
    public bool ExplosionAllowLifeSteal = false;
    [Tooltip("Split fragment damage = root damage × falloff^generation.")]
    public float SplitDamageFalloff = 0.6f;
    [Tooltip("Hard cap on Split generations (DepthLeft = min(FinalSplitDepth, this)).")]
    public int MaxSplitDepth = 3;
    [Tooltip("Max children spawned per lineage (original cast), explosions and fragments included.")]
    public int LineageSpawnBudget = 128;
    [Tooltip("Seconds before a contact damager can hit the same target again (SpellSO.ReHitDelayOverride overrides).")]
    public float DefaultReHitDelay = 1f;
    [Tooltip("Hard global cap of children spawned per frame; requests beyond are dropped and counted.")]
    public int MaxEmitPerFrame = 256;

    public EmitterConfig ToConfig() => new EmitterConfig
    {
        ExplosionDamageRatio = ExplosionDamageRatio,
        ExplosionAllowLifeSteal = ExplosionAllowLifeSteal,
        SplitDamageFalloff = SplitDamageFalloff,
        MaxSplitDepth = MaxSplitDepth,
        LineageSpawnBudget = LineageSpawnBudget,
        DefaultReHitDelay = DefaultReHitDelay,
        MaxEmitPerFrame = MaxEmitPerFrame,
    };
}
