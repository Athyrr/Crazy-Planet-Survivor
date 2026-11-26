using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;

/// <summary>Per-instance VAT playback written to _VATFrame: x = row A, y = row B (absolute, fractional), z = blend, w unused.</summary>
[MaterialProperty(VATShaderProperties.Frame)]
public struct VATFrame : IComponentData
{
    public float4 Value;
}

/// <summary>Runtime state of one VAT instance: measured movement, smoothed speed and the shared normalized phase.</summary>
public struct VATAnimationState : IComponentData
{
    public float3 PrevPosition;
    public float Phase;
    public float SmoothedSpeed;
    public byte Initialized;
}

/// <summary>Clip table of the model, baked from its VATAnimationSO (deduplicated across instances).</summary>
public struct VATClipTable : IComponentData
{
    public BlobAssetReference<VATClipTableBlob> Value;
}
