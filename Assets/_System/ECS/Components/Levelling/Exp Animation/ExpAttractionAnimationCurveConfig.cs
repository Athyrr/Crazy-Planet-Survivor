using Unity.Entities;

/// <summary>
/// Exp orb animation configuration component.
/// </summary>
public struct ExpAttractionAnimationCurveConfig : IComponentData
{
    public BlobAssetReference<ExpAttractionAnimationCurveBlob> CurveBlobRef;
}
