using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Helper for planet based movement calculations. Burst compatible so can be used in Jobs.
/// </summary>
// @todo heightmap sampling
[BurstCompile]
public static partial class PlanetMovementUtils
{
    #region Fixed radius version API

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

    #endregion


    #region Height map version API

    /// <summary>
    /// 
    /// </summary>
    /// <param name="position">World position</param>
    /// <param name="localUV">The cube face UV.</param>
    /// <param name="faceIndex">Cube face index</param>
    [BurstCompile]
    private static void GetCubeFace(in float3 position, out float3 localUV, out int faceIndex)
    {
        localUV = float3.zero;
        faceIndex = -1;

        float absX = math.abs(position.x);
        float absY = math.abs(position.y);
        float absZ = math.abs(position.z);


        // Get dominant axis

        // +X / -X face
        if (absX >= absY && absX >= absZ)
        {
            float inverseX = 1f / absX;
            localUV = new float3(position.y * inverseX, position.z * inverseX, 0);

            faceIndex = position.x > 0 ? 0 : 1;
        }

        // +Y / -Y face
        else if (absY >= absX && absY >= absZ)
        {
            float inverseY = 1f / absY;
            localUV = new float3(position.x * inverseY, position.z * inverseY, 0);

            faceIndex = position.y > 0 ? 2 : 3;
        }

        // +Z / -Z face
        else
        {
            float inverseZ = 1f / absZ;
            localUV = new float3(position.x * inverseZ, position.y * inverseZ, 0);

            faceIndex = position.z > 0 ? 4 : 5;
        }
    }

    /// <summary>
    /// Gets world positon to heightmap index (native array index)
    /// </summary>
    /// <param name="worldPosition">World position</param>
    /// <param name="planetData">Planet data</param>
    /// <param name="localUV">local uv coord for a face</param>
    /// <param name="heightmapIndex">Output index for heightmap sampling</param>
    [BurstCompile]
    private static void GetHeightMapIndexFromPosition(in float3 worldPosition, in float3 planetCenter, in PlanetData planetData, out int heightmapIndex)
    {
        // Get relative position to planet center
        float3 relativePosition = worldPosition - planetCenter;

        // Cube face index
        GetCubeFace(relativePosition, out float3 localUV, out int faceIndex);

        // Remap coord [-1,1] into [0,1]
        float u = localUV.x * 0.5f + 0.5f;
        float v = localUV.y * 0.5f + 0.5f;

        int resolution = planetData.FaceResolution;

        // UV coord using heightmap resolution
        int uIndex = math.clamp((int)(u * (resolution - 1)), 0, resolution - 1); // Clamp
        int vIndex = math.clamp((int)(v * (resolution - 1)), 0, resolution - 1); // Clamp

        heightmapIndex = (faceIndex * (resolution * resolution)) + (vIndex * resolution) + uIndex;
        Debug.LogWarning($"Face: {faceIndex} | UV: {uIndex},{vIndex} | Index: {heightmapIndex}");
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="worldPosition"></param>
    /// <param name="planetData"></param>
    /// <param name="height"></param>
    [BurstCompile]
    private static void GetSurfaceHeightAtPosition(in float3 worldPosition, in float3 planetCenter, [ReadOnly] ref PlanetData planetData, out float height)
    {
        ref var heightMapBlob = ref planetData.HeightDataReference.Value;

        GetHeightMapIndexFromPosition(worldPosition, planetCenter, planetData, out int index);

        index = math.clamp(index, 0, heightMapBlob.Heights.Length - 1);
        float heightmapValue = heightMapBlob.Heights[index];
        //Debug.LogWarning($"Heightmap value at index {index}: {heightmapValue}");

        height = heightmapValue * planetData.MaxHeight;
    }

    [BurstCompile]
    public static void SnapToSurfaceHeightMap(in float3 position, in float3 planetCenter, [ReadOnly] ref PlanetData planetData, out float3 snappedPosition)
    {
        GetSurfaceNormalAtPosition(position, planetCenter, out float3 normal);
        GetSurfaceHeightAtPosition(position, planetCenter, ref planetData, out float height);

        snappedPosition = planetData.Center + normal * (planetData.Radius + height);

        //Debug.LogWarning($"SnapToSurfaceHeightMap: Height={height} | SnappedPos={snappedPosition}");
    }

    [BurstCompile]
    public static void GetSurfaceNormalAtPositionHeightMap(in float3 position, in float3 planetCenter, out float3 normal)
    {
        normal = math.normalize(position - planetCenter);
        return;
    }

    #endregion
}