using Unity.Entities;

public struct EffectTypeConfigEntry
{
    public EffectType Type;
    public float Ratio;
    public float BaseMagnitude;
    public float BaseDuration;
    public float TickRate;
    public EStackMode StackMode;
    public int MaxStacks;
    public bool AllowCrit;
    public bool AllowLifeSteal;
}

public struct EffectTypeConfigBlob
{
    public BlobArray<EffectTypeConfigEntry> Entries;
    public BlobArray<float> KnockbackForceCurveSamples;
}

public struct EffectTypeConfig : IComponentData
{
    public BlobAssetReference<EffectTypeConfigBlob> Blob;
}

public static class EffectTypeConfigLookup
{
    /// <summary>Linear scan over 4 entries — cheap, Burst-safe. Falls back to entry 0 (Burn) if a type is
    /// missing from the SO, which is an authoring bug, not something that should crash at runtime.</summary>
    public static ref readonly EffectTypeConfigEntry Get(ref BlobArray<EffectTypeConfigEntry> entries, EffectType type)
    {
        for (int i = 0; i < entries.Length; i++)
            if (entries[i].Type == type)
                return ref entries[i];
        return ref entries[0];
    }
}
