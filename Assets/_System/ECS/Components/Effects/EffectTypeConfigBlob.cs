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
    /// <summary>Zero-valued fallback for a genuinely empty blob (EffectTypeConfigAuthoring.Settings left
    /// unassigned — see I5, final whole-branch review: the old `entries[0]` fallback threw
    /// IndexOutOfRangeException instead of degrading gracefully in exactly that case).</summary>
    private static readonly EffectTypeConfigEntry EmptyEntry = default;

    /// <summary>Linear scan over 4 entries — cheap, Burst-safe. Falls back to entry 0 (Burn) if a type is
    /// missing from the SO, which is an authoring bug, not something that should crash at runtime. Falls
    /// back further to a zero-valued entry if the blob has no entries at all (empty array, not just a
    /// missing type) — never indexes an empty BlobArray.</summary>
    public static ref readonly EffectTypeConfigEntry Get(ref BlobArray<EffectTypeConfigEntry> entries, EffectType type)
    {
        for (int i = 0; i < entries.Length; i++)
            if (entries[i].Type == type)
                return ref entries[i];
        if (entries.Length == 0)
            return ref EmptyEntry;
        return ref entries[0];
    }
}
