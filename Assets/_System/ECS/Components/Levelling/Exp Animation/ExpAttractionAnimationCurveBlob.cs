using Unity.Entities;
using UnityEngine;

/// <summary>
/// Exp gems animation curve blob.
/// </summary>
public struct ExpAttractionAnimationCurveBlob
{
    public BlobArray<float> Samples;
    public float Duration;
    public int SampleCount;
}
