using UnityEngine;

/// <summary>Material property names shared by VATBaker, VATAnimationSO.ApplyTo, the ECS components and VATAnimationPlayer.</summary>
public static class VATShaderProperties
{
    public const string Texture = "_Texture2D";
    public const string TotalVertices = "_totalVertices";
    public const string RowsPerBlock = "_VATRowsPerBlock";
    public const string Bounds = "_bounds";
    public const string AddPositionMode = "_addPositionMode";
    public const string NormalIsBaked = "_normalIsBaked";
    public const string ObjectOffsetMaxDistance = "_objectOffsetMaxDistance";
    /// <summary>Per instance (Hybrid Per Instance): x = row A, y = row B (absolute, fractional), z = blend, w unused.</summary>
    public const string Frame = "_VATFrame";

    public static readonly int FrameId = Shader.PropertyToID(Frame);
}
