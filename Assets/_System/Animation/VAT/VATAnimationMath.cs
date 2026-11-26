using Unity.Mathematics;

/// <summary>
/// Shared VAT playback math (pure, Burst-friendly): picks the two clips around the speed, integrates one normalized
/// phase shared by both, and returns the _VATFrame value. Called by VATAnimationSystem (Burst job) and VATAnimationPlayer.
/// </summary>
public static class VATAnimationMath
{
    /// <summary>Frame-rate independent exponential smoothing: s += (raw - s) * (1 - exp(-dt / tau)).</summary>
    public static float SmoothSpeed(float smoothed, float raw, float dt, float smoothingTime)
        => smoothingTime <= 0f ? raw : smoothed + (raw - smoothed) * (1f - math.exp(-dt / smoothingTime));

    /// <summary>True when a raw speed is a teleport. A zero threshold (single clip at speed 0) never rejects.</summary>
    public static bool IsTeleport(float rawSpeed, float teleportSpeed) => teleportSpeed > 0f && rawSpeed > teleportSpeed;

    /// <summary>Neighbour clips of a speed and the weight of clipB. One clip, or outside the table: one clip alone.</summary>
    public static void SelectClips(ref VATClipTableBlob table, float speed, out int clipA, out int clipB, out float blend)
        => SelectClips(ref table, speed, out clipA, out clipB, out blend, out _);

    /// <summary>
    /// Same, plus the cadence scale of clipA. With t the linear position between the two native speeds and w the
    /// CrossfadeWidth: w = 1 gives blend = t (linear over the whole interval) and a scale of 1; w &lt; 1 gives
    /// blend = smoothstep(1 - w, 1, t), and clipA plays sped up by speed / its native speed up to the band start
    /// (scale frozen inside the band), so its legs do not slide while it plays alone.
    /// </summary>
    public static void SelectClips(ref VATClipTableBlob table, float speed, out int clipA, out int clipB, out float blend, out float lowerScale)
    {
        lowerScale = 1f;
        int count = table.Clips.Length;
        int last = count - 1;
        if (count <= 1 || speed <= table.Clips[0].NativeSpeed) { clipA = 0; clipB = 0; blend = 0f; return; }
        if (speed >= table.Clips[last].NativeSpeed) { clipA = last; clipB = last; blend = 0f; return; }

        int upper = 1;
        while (speed > table.Clips[upper].NativeSpeed) upper++;
        clipA = upper - 1;
        clipB = upper;
        float low = table.Clips[clipA].NativeSpeed;
        float high = table.Clips[clipB].NativeSpeed;
        float t = math.saturate((speed - low) / (high - low));

        float width = table.CrossfadeWidth;
        if (width >= 1f) { blend = t; return; }   // whole interval: the former linear blend, unchanged

        float bandStart = 1f - width;
        blend = math.smoothstep(bandStart, 1f, t);
        if (low > 0f) lowerScale = math.min(speed, low + bandStart * (high - low)) / low;   // an idle (speed 0) is never sped up
    }

    /// <summary>1 up to the native speed of the fastest clip, then speed / native, capped by MaxPlaybackScale.</summary>
    public static float PlaybackScale(ref VATClipTableBlob table, float speed)
    {
        int count = table.Clips.Length;
        if (count <= 1) return 1f;
        float native = table.Clips[count - 1].NativeSpeed;
        if (native <= 0f || speed <= native) return 1f;
        return math.min(speed / native, table.MaxPlaybackScale);
    }

    /// <summary>Advances the shared phase and returns (FrameA, FrameB, Blend, 0) for _VATFrame.</summary>
    public static float4 Step(ref VATClipTableBlob table, float speed, float dt, ref float phase)
    {
        SelectClips(ref table, speed, out int a, out int b, out float blend, out float lowerScale);
        ref var clipA = ref table.Clips[a];
        ref var clipB = ref table.Clips[b];
        float rate = math.lerp(lowerScale / clipA.Duration, 1f / clipB.Duration, blend) * PlaybackScale(ref table, speed);
        phase = math.frac(phase + dt * rate);
        return new float4(Row(clipA, phase), Row(clipB, phase), blend, 0f);
    }

    /// <summary>Absolute fractional row of a clip at a phase in [0, 1); the sampler interpolates between rows.</summary>
    public static float Row(in VATClip clip, float phase) => clip.StartRow + phase * clip.FrameCount;
}
