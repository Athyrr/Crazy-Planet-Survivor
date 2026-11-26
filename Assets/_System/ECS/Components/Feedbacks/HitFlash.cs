using Unity.Entities;
using Unity.Rendering;

/// <summary>
/// Per-instance hit flash written to the _HitFlash material property (SG_VAT_Master, SG_VAT_Master_Legacy).
/// Disabled at rest; HitFlashSystem enables it at 5 and decays it to 0. Baked by HitFlashBaker.
/// </summary>
[MaterialProperty("_HitFlash")]
public struct HitFlash : IComponentData, IEnableableComponent
{
    public float Value;
}

/// <summary>One-frame request: flash the target, or every member of its LinkedEntityGroup that carries HitFlash.</summary>
public struct HitFlashRequest : IComponentData
{
    public Entity TargetEntity;
}
