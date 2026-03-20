using Unity.Entities;

/// <summary>
/// Exp orb animation configuration component.
/// </summary>
public struct AttractionAnimationCurveConfig : IComponentData
{
    public BlobAssetReference<AttractionAnimationCurveBlob> CurveBlobRef;
}
