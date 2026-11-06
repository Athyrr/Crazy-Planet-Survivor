using Unity.Entities;

/// <summary>Global emitter rules (CpEmitterSettings), baked by EffectTypeConfigAuthoring. When the singleton is
/// missing, every reader falls back to Default (SpellEmissionSystem warns once).</summary>
public struct EmitterConfig : IComponentData
{
    public float ExplosionDamageRatio;
    public bool ExplosionAllowLifeSteal;
    public float SplitDamageFalloff;
    public int MaxSplitDepth;
    public int LineageSpawnBudget;
    public float DefaultReHitDelay;
    public int MaxEmitPerFrame;

    public static EmitterConfig Default => new EmitterConfig
    {
        ExplosionDamageRatio = 1f,
        ExplosionAllowLifeSteal = false,
        SplitDamageFalloff = 0.6f,
        MaxSplitDepth = 3,
        LineageSpawnBudget = 128,
        DefaultReHitDelay = 1f,
        MaxEmitPerFrame = 256,
    };
}
