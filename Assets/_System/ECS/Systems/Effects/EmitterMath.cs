using Unity.Mathematics;

/// <summary>Pure emitter arithmetic (no ECS state) so EditMode tests can pin it down. Used by ResolveHit
/// (falloff), AreaAttackSystem (radius ease), SpellEmissionSystem (layouts, budget, chance) and
/// EmitterAnchorSystem (orbit positions).</summary>
public static class EmitterMath
{
    public static float Falloff(float perGeneration, int generation)
        => generation <= 0 ? 1f : math.pow(math.max(0f, perGeneration), generation);

    public static float EaseRadiusT(ERadiusEase ease, float t)
    {
        t = math.saturate(t);
        float u = 1f - t;
        switch (ease)
        {
            case ERadiusEase.EaseOutQuad: return 1f - u * u;
            case ERadiusEase.EaseOutCubic: return 1f - u * u * u;
            default: return t;
        }
    }

    public static float ExplosionScale(float finalExplosionSize) => math.max(0.1f, 1f + finalExplosionSize);

    public static float SpreadAngleDegrees(int index, int count, float perAngleDegrees)
        => count <= 1 ? 0f : (index - (count - 1) * 0.5f) * perAngleDegrees;

    public static float CircleAngleRadians(float baseAngleRadians, int index, int count)
        => count <= 0 ? baseAngleRadians : baseAngleRadians + 2f * math.PI * index / count;

    public static float OrbitRadius(float entryRadius, float finalRange, float baseCastRange)
        => entryRadius * (finalRange / math.max(baseCastRange, 1e-4f));

    public static float3 TangentReference(float3 normal, float3 hint)
    {
        float3 t = hint - math.dot(hint, normal) * normal;
        if (math.lengthsq(t) < 1e-6f)
        {
            float3 alt = math.abs(normal.x) < 0.9f ? new float3(1f, 0f, 0f) : new float3(0f, 0f, 1f);
            t = alt - math.dot(alt, normal) * normal;
        }
        return math.normalize(t);
    }

    public static float3 OrbitOffset(float3 normal, float3 reference, float radius, float angleRadians)
    {
        float3 side = math.cross(normal, reference);
        math.sincos(angleRadians, out float s, out float c);
        return radius * (c * reference + s * side);
    }

    public static bool ChanceRoll(float roll01, float chance) => chance >= 1f || roll01 < chance;

    public static int ClampToBudget(int requested, int budgetLeft) => math.clamp(requested, 0, math.max(0, budgetLeft));
}
