using Unity.Entities;

/// <summary>
/// Baked, Burst-readable snapshot of the gameplay-relevant rarity numbers from
/// <c>CpRaritySettings</c> (weights, luck curve, spell cadence). Visuals stay in the SO.
/// </summary>
public struct RaritySettings : IComponentData
{
    public BlobAssetReference<RaritySettingsBlob> Blob;
}

public struct RaritySettingsBlob
{
    /// <summary>Every Nth level offers spells instead of stat upgrades.</summary>
    public int SpellDropLevelInterval;

    /// <summary>Luck value that maps to the upper bound (1.0) of the sampled luck curve.</summary>
    public float MaxLuck;

    /// <summary>Base drop weight per tier, indexed by (int)ERarity. Length = RarityConstants.Count.</summary>
    public BlobArray<float> BaseWeights;

    /// <summary>
    /// Luck→rare-weight multiplier, sampled uniformly over normalized luck [0..1].
    /// Final tier weight = BaseWeights[t] * pow(luckFactor, t).
    /// </summary>
    public BlobArray<float> LuckSamples;
}
