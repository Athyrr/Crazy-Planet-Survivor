using System;
using UnityEngine;

/// <summary>
/// Baked VAT atlas of one model: every clip one under the other in a single texture (each clip followed by a copy
/// of its frame 0, normals in the lower half). Written by VATBaker; NativeSpeed and the playback settings are tuned
/// by hand and kept across rebakes.
/// </summary>
public class VATAnimationSO : ScriptableObject
{
    [Header("Baked (written by VATBaker)")]
    public Texture2D Texture;
    public int VertexCount;
    [Tooltip("Rows of one vertex block: sum of (FrameCount + 1) over the clips.")]
    public int RowsPerBlock;
    public int SampleRate;
    public Vector3 BoundsSize;
    public Vector3 ObjectOffsetMaxDistance;
    public bool ObjectSpace;
    public bool NormalsBaked;
    public VATClipInfo[] Clips = Array.Empty<VATClipInfo>();

    [Header("Playback (tuned by hand)")]
    [Tooltip("Above the native speed of the fastest clip, the cadence follows the speed up to this factor. " +
             "Caps only above the fastest clip: the speed-up or slow-down of a clip inside the table (CrossfadeWidth < 1) is not capped.")]
    [Min(1f)] public float MaxPlaybackScale = 2f;
    [Tooltip("Width of the crossfade, as a fraction of the interval between two neighbouring native speeds. " +
             "1 = linear blend over the whole interval between two clips, at native cadences (the former behaviour). " +
             "Below 1 the curve changes shape (smoothstep, cadence following the speed), so 0.99 and 1 differ visibly. " +
             "0.2 = stay on the lower clip, sped up with the speed, and crossfade over the last 20 % before the upper clip's native speed; " +
             "from an idle (native speed 0) the upper clip takes over over the first 20 % instead, slowed down to the speed.")]
    [Range(0.01f, 1f)] public float CrossfadeWidth = 1f;
    [Tooltip("Time constant (s) of the exponential smoothing of the measured speed.")]
    [Min(0f)] public float SpeedSmoothingTime = 0.1f;
    [Tooltip("A raw speed above this factor x the highest NativeSpeed is a teleport and is ignored.")]
    [Min(1f)] public float TeleportSpeedFactor = 8f;

    /// <summary>Writes the texture and the decoding settings into a material on SG_VAT_Master.</summary>
    public void ApplyTo(Material material)
    {
        material.SetTexture(VATShaderProperties.Texture, Texture);
        material.SetFloat(VATShaderProperties.TotalVertices, VertexCount);
        material.SetFloat(VATShaderProperties.RowsPerBlock, RowsPerBlock);
        material.SetVector(VATShaderProperties.Bounds, BoundsSize);
        material.SetFloat(VATShaderProperties.AddPositionMode, ObjectSpace ? 1f : 0f);
        material.SetFloat(VATShaderProperties.NormalIsBaked, NormalsBaked ? 1f : 0f);
        material.SetVector(VATShaderProperties.ObjectOffsetMaxDistance, ObjectOffsetMaxDistance);
    }
}

[Serializable]
public struct VATClipInfo
{
    public string Name;
    [Tooltip("First row of the clip in each vertex block.")]
    public int StartRow;
    [Tooltip("Sampled frames, without the buffer row.")]
    public int FrameCount;
    [Tooltip("Seconds of one cycle: FrameCount / SampleRate.")]
    public float Duration;
    public bool Loop;
    [Tooltip("Movement speed (world units/s) at which this clip does not slide. Tuned by hand, kept across rebakes.")]
    public float NativeSpeed;
}
