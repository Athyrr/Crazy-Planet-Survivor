using Unity.Burst;
using Unity.Mathematics;

/// <summary>
/// Helper planet based movement calculations. Burst compatible so can be used in Jobs.
/// </summary>
[BurstCompile]
public static partial class PlanetMovementUtils
{
    [BurstCompile]
    public static float3 ProjectDirectionOnSurface(in float3 direction, in float3 normal)
    {
        float3 tangent = direction - math.dot(direction, normal) * normal;
        return math.lengthsq(tangent) > 0.001f ? math.normalize(tangent) : float3.zero;
    }

    [BurstCompile]
    public static float3 SnapToSurface(in float3 position, in float3 planetCenter, float planetRadius)
    {
        return planetCenter + math.normalize(position - planetCenter) * planetRadius;
    }

    [BurstCompile]
    public static quaternion GetRotationOnSurface(in float3 to, in float3 normal)
    {
        return quaternion.LookRotationSafe(to, normal);
    }

    [BurstCompile]
    public static float3 GetSurfaceNormalAtPosition(in float3 position, in float3 center)
    {
        return math.normalize(position - center);
    }

    [BurstCompile]
    public static float3 GetSurfaceStepTowardPosition(in float3 from, in float3 toPosition, float distance, in float3 planetCenter, float radius)
    {
        float3 normal = GetSurfaceNormalAtPosition(in from,in planetCenter);
        float3 tangentialDirection = ProjectDirectionOnSurface(math.normalize(toPosition - from),in normal);
        float3 newPosition = from + tangentialDirection * distance;
        return SnapToSurface(newPosition, planetCenter, radius);
    }


    [BurstCompile]
    public static float3 GetSurfaceStepTowardDirection(in float3 from, in float3 toDirection, float distance, in float3 planetCenter, float radius)
    {
        float3 normal = GetSurfaceNormalAtPosition(from, planetCenter);
        float3 tangentialDirection = ProjectDirectionOnSurface(math.normalize(toDirection), normal);
        float3 newPosition = from + tangentialDirection * distance;
        return SnapToSurface(newPosition, planetCenter, radius);
    }

    [BurstCompile]
    public static float GetSurfaceDistance(in float3 from, in float3 to, in float3 planetCenter, float planetRadius)
    {
        float3 fromDir = math.normalize(from - planetCenter);
        float3 toDir = math.normalize(to - planetCenter);
        float angle = math.acos(math.clamp(math.dot(fromDir, toDir), -1f, 1f));
        return angle * planetRadius;
    }

    [BurstCompile]
    public static bool IsWithinRange(in float3 center, in float3 target, float range, in float3 planetCenter, float planetRadius)
    {
        return GetSurfaceDistance(center, target, planetCenter, planetRadius) <= range;
    }
}