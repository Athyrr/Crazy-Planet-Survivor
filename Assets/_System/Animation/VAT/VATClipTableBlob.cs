using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>One clip of a VAT atlas, as read at runtime.</summary>
public struct VATClip
{
    public int StartRow;
    public int FrameCount;
    public float Duration;
    public float NativeSpeed;
    public bool Loop;
}

/// <summary>Clip table of a VAT atlas, sorted by NativeSpeed (ascending). Shared by every instance of a model.</summary>
public struct VATClipTableBlob
{
    public BlobArray<VATClip> Clips;
    /// <summary>Raw speeds above this are teleports (TeleportSpeedFactor x highest NativeSpeed).</summary>
    public float TeleportSpeed;
    public float MaxPlaybackScale;
    public float SpeedSmoothingTime;
    /// <summary>Crossfade width as a fraction of the interval between two native speeds, in (0, 1]; 1 = linear blend over the whole interval.</summary>
    public float CrossfadeWidth;
}

/// <summary>Builds the clip table blob, the same way for the ECS baker and for VATAnimationPlayer.</summary>
public static class VATClipTableBuilder
{
    private const float SameSpeedEpsilon = 1e-4f;
    private const float MinCrossfadeWidth = 0.01f;   // a zero-width smoothstep divides by zero

    public static BlobAssetReference<VATClipTableBlob> Build(VATAnimationSO animation, Allocator allocator)
    {
        var source = animation.Clips ?? Array.Empty<VATClipInfo>();
        var clips = new VATClip[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            var c = source[i];
            clips[i] = new VATClip { StartRow = c.StartRow, FrameCount = c.FrameCount, Duration = c.Duration, NativeSpeed = c.NativeSpeed, Loop = c.Loop };
        }
        return Build(clips, animation.TeleportSpeedFactor, animation.MaxPlaybackScale, animation.SpeedSmoothingTime, allocator, animation.CrossfadeWidth);
    }

    public static BlobAssetReference<VATClipTableBlob> Build(VATClip[] clips, float teleportSpeedFactor, float maxPlaybackScale, float speedSmoothingTime, Allocator allocator, float crossfadeWidth = 1f)
    {
        var sorted = (VATClip[])clips.Clone();
        Array.Sort(sorted, (a, b) => a.NativeSpeed.CompareTo(b.NativeSpeed));

        float maxNative = 0f;
        foreach (var c in sorted) maxNative = math.max(maxNative, c.NativeSpeed);

        var builder = new BlobBuilder(Allocator.Temp);
        ref var root = ref builder.ConstructRoot<VATClipTableBlob>();
        var array = builder.Allocate(ref root.Clips, sorted.Length);
        for (int i = 0; i < sorted.Length; i++)
        {
            var c = sorted[i];
            c.Duration = math.max(c.Duration, 1e-3f);
            array[i] = c;
        }
        root.TeleportSpeed = teleportSpeedFactor * maxNative;
        root.MaxPlaybackScale = math.max(1f, maxPlaybackScale);
        root.SpeedSmoothingTime = math.max(0f, speedSmoothingTime);
        root.CrossfadeWidth = math.clamp(crossfadeWidth, MinCrossfadeWidth, 1f);

        var result = builder.CreateBlobAssetReference<VATClipTableBlob>(allocator);
        builder.Dispose();
        return result;
    }

    public static bool HasDuplicateNativeSpeeds(VATAnimationSO animation)
    {
        var clips = animation.Clips ?? Array.Empty<VATClipInfo>();
        for (int i = 0; i < clips.Length; i++)
            for (int j = i + 1; j < clips.Length; j++)
                if (math.abs(clips[i].NativeSpeed - clips[j].NativeSpeed) < SameSpeedEpsilon)
                    return true;
        return false;
    }
}
