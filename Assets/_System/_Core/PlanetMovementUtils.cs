using Unity.Burst;
using Unity.Mathematics;

/// <summary>
/// Helper for planet based movement calculations. Burst compatible so can be used in Jobs.
/// </summary>
[BurstCompile]
public static partial class PlanetMovementUtils
{
    [BurstCompile]
    public static void ProjectDirectionOnSurface(in float3 direction, in float3 normal, out float3 projectedDirection)
    {
        float3 tangent = direction - math.dot(direction, normal) * normal;
        projectedDirection = math.lengthsq(tangent) > 0.001f ? math.normalize(tangent) : float3.zero;
        return;
    }

    [BurstCompile]
    public static void SnapToSurface(in float3 position, in float3 planetCenter, float planetRadius, out float3 snappedPosition)
    {
        snappedPosition = planetCenter + math.normalize(position - planetCenter) * planetRadius;
        return;
    }

    [BurstCompile]
    public static void GetRotationOnSurface(in float3 to, in float3 normal, out quaternion rotation)
    {
        rotation = quaternion.LookRotationSafe(to, normal);
        return;
    }

    [BurstCompile]
    public static void GetSurfaceNormalAtPosition(in float3 position, in float3 center, out float3 normal)
    {
        normal = math.normalize(position - center);
        return;
    }

    [BurstCompile]
    public static void GetSurfaceStepTowardPosition(in float3 from, in float3 toPosition, float distance, in float3 planetCenter, float radius, out float3 resultPosition)
    {
        GetSurfaceNormalAtPosition(in from, in planetCenter, out var normal);
        ProjectDirectionOnSurface(math.normalize(toPosition - from), in normal, out float3 tangentDirection);
        float3 newPosition = from + tangentDirection * distance;
        SnapToSurface(newPosition, planetCenter, radius, out var snapped);
        resultPosition = snapped;
        return;
    }


    [BurstCompile]
    public static void GetSurfaceStepTowardDirection(in float3 from, in float3 toDirection, float distance, in float3 planetCenter, float radius, out float3 resultPosition)
    {
        GetSurfaceNormalAtPosition(from, planetCenter, out var normal);
        ProjectDirectionOnSurface(math.normalize(toDirection), normal, out float3 tangentDirection);
        float3 newPosition = from + tangentDirection * distance;
        SnapToSurface(newPosition, planetCenter, radius, out var snappedPosition);
        resultPosition = snappedPosition;
        return;
    }

    [BurstCompile]
    public static void GetSurfaceDistanceBetweenPoints(in float3 from, in float3 to, in float3 planetCenter, float planetRadius, out float distance)
    {
        float3 fromDir = math.normalize(from - planetCenter);
        float3 toDir = math.normalize(to - planetCenter);
        float angle = math.acos(math.clamp(math.dot(fromDir, toDir), -1f, 1f));
        distance = angle * planetRadius;
        return;
    }

    [BurstCompile]
    public static void IsWithinRange(in float3 center, in float3 target, float range, in float3 planetCenter, float planetRadius, out bool result)
    {
        GetSurfaceDistanceBetweenPoints(center, target, planetCenter, planetRadius, out var distance);
        result = distance <= range;
        return;
    }
}