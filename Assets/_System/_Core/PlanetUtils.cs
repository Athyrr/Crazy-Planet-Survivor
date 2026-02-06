using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Physics;

/// <summary>
/// Helper class for planet-based movement calculations.
/// This class is static, Burst-compatible, and can be used in jobs.
/// It provides multiple APIs for different planet surface models:
/// 1. Fixed Radius: A perfect sphere.
/// 2. Collision: A complex mesh, queried using raycasts (Unity.Physics).
/// 3. Heightmap: A perfect sphere offset by a heightmap (Blob Asset).
/// </summary>
[BurstCompile]
public static partial class PlanetUtils
{
    #region Core

    /// <summary>
    /// Projects a direction vector onto a surface tangent, defined by its normal.
    /// </summary>
    /// <param name="direction">The world-space direction to project.</param>
    /// <param name="normal">The "up" vector (normal) of the surface.</param>
    /// <param name="projectedDirection">The resulting projected direction.</param>
    [BurstCompile]
    public static void ProjectDirectionOnSurface(in float3 direction, in float3 normal, out float3 projectedDirection)
    {
        float3 tangent = direction - math.dot(direction, normal) * normal;
        projectedDirection = math.lengthsq(tangent) > 0.001f ? math.normalize(tangent) : float3.zero;
    }

    /// <summary>
    /// Gets a quaternion rotation that aligns an entity to a surface.
    /// </summary>
    /// <param name="forward">The desired "forward" direction, tangent to the surface.</param>
    /// <param name="normal">The desired "up" direction (the surface normal).</param>
    /// <param name="rotation">The resulting object rotation.</param>
    [BurstCompile]
    public static void GetRotationOnSurface(in float3 to, in float3 normal, out quaternion rotation)
    {
        rotation = quaternion.LookRotationSafe(to, normal);
        return;
    }

    #endregion


    #region Fixed Radius API (Perfect Sphere)

    /// <summary>
    /// Snaps a world position to the surface of a perfect sphere.
    /// </summary>
    /// <param name="position">The world position.</param>
    /// <param name="planetCenter">The center of the planet.</param>
    /// <param name="planetRadius">The radius of the planet.</param>
    /// <param name="snappedPosition">The resulting position on the sphere's surface.</param>
    [BurstCompile]
    public static void SnapToSurfaceRadius(in float3 position, in float3 planetCenter, float planetRadius, out float3 snappedPosition)
    {
        snappedPosition = planetCenter + math.normalize(position - planetCenter) * planetRadius;
    }

    /// <summary>
    /// Gets the surface normal (up vector) at a position on a perfect sphere.
    /// </summary>
    /// <param name="position">The world position to check from.</param>
    /// <param name="center">The center of the planet.</param>
    /// <param name="normal">The resulting "up" vector (normal).</param>
    [BurstCompile]
    public static void GetSurfaceNormalRadius(in float3 position, in float3 center, out float3 normal)
    {
        normal = math.normalize(position - center);
    }

    /// <summary>
    /// Calculates a new position after moving a set distance towards a target, following a perfect sphere's curvature.
    /// </summary>
    [BurstCompile]
    public static void GetSurfaceStepTowardPositionRadius(in float3 from, in float3 toPosition, float distance, in float3 planetCenter, float radius, out float3 resultPosition)
    {
        GetSurfaceNormalRadius(in from, in planetCenter, out var normal);
        ProjectDirectionOnSurface(math.normalize(toPosition - from), in normal, out float3 tangentDirection);
        float3 newPosition = from + tangentDirection * distance;

        SnapToSurfaceRadius(newPosition, planetCenter, radius, out resultPosition);
        return;
    }

    /// <summary>
    /// Calculates a new position after moving a set distance in a direction, following a perfect sphere's curvature.
    /// </summary>
    [BurstCompile]
    public static void GetSurfaceStepTowardDirectionRadius(in float3 from, in float3 toDirection, float distance, in float3 planetCenter, float radius, out float3 resultPosition)
    {
        GetSurfaceNormalRadius(from, planetCenter, out var normal);
        ProjectDirectionOnSurface(math.normalize(toDirection), normal, out float3 tangentDirection);
        float3 newPosition = from + tangentDirection * distance;
        SnapToSurfaceRadius(newPosition, planetCenter, radius, out resultPosition);
    }

    /// <summary>
    /// Gets the "arc" distance between two points on a perfect sphere's surface.
    /// </summary>
    [BurstCompile]
    public static void GetSurfaceDistanceRadius(in float3 from, in float3 to, in float3 planetCenter, float planetRadius, out float distance)
    {
        float3 fromDir = math.normalize(from - planetCenter);
        float3 toDir = math.normalize(to - planetCenter);
        float angle = math.acos(math.clamp(math.dot(fromDir, toDir), -1f, 1f));
        distance = angle * planetRadius;
    }

    /// <summary>
    /// Checks if a target is within a given "arc" distance on a perfect sphere.
    /// </summary>
    [BurstCompile]
    public static void IsWithinRangeRadius(in float3 center, in float3 target, float range, in float3 planetCenter, float planetRadius, out bool result)
    {
        GetSurfaceDistanceRadius(center, target, planetCenter, planetRadius, out var distance);
        result = distance <= range;
    }

    #endregion


    #region Collision API (Physics Mesh)

    /// <summary>
    /// Snaps a position to the ground by raycasting towards planet center.
    /// /!\ For mesh-based planets.
    /// </summary>
    /// <param name="collisionWorld">The physics world to raycast against.</param>
    /// <param name="position">The world position to snap from.</param>
    /// <param name="planetCenter">The center of the planet.</param>
    /// <param name="filter">The collision filter (The planet layer).</param>
    /// <param name="startRayHeight">How far to cast the ray.</param>
    /// <param name="hit">The resulting RaycastHit.</param>
    /// <returns>True if the raycast hit the planet.</returns>
    [BurstCompile]
    public static bool SnapToSurfaceRaycast(
        [ReadOnly] ref CollisionWorld collisionWorld,
       in float3 position,
        in float3 planetCenter,
       in CollisionFilter filter,
        float startRayHeight,
        out RaycastHit hit)
    {
        // Define "down" as the vector towards the planet center
        float3 normal = math.normalize(position - planetCenter);

        // Start ray little "above" the position to avoid starting inside the mesh
        float3 rayStart = position + normal * startRayHeight;

        // End ray below the position
        float3 rayEnd = position - normal * startRayHeight;

        var rayInput = new RaycastInput
        {
            Start = rayStart,
            End = rayEnd,
            Filter = filter
        };

        return collisionWorld.CastRay(rayInput, out hit);
    }

    /// <summary>
    /// Get a random point on the planet surface within a given range around an origin point.
    /// </summary>
    /// <param name="collisionWorld">Collision world reference.</param>
    /// <param name="random">Random struct reference.</param>
    /// <param name="centerPos">Origin point around which the point is.</param>
    /// <param name="planetCenter">Planet center postion.</param>
    /// <param name="range">Range radius.</param>
    /// <param name="filter">Collision filter.</param>
    /// <param name="foundPosition">Found position.</param>
    /// <returns>Returns true if a point was found succesfully.</returns>
    [BurstCompile]
    public static bool GetRandomPointOnSurface(
        [ReadOnly] ref CollisionWorld collisionWorld,
        ref Random random,
       in float3 centerPos,
       in float3 planetCenter,
        float range,
       ref CollisionFilter filter,
        out float3 foundPosition)
    {
        float3 up = math.normalize(centerPos - planetCenter);

        float2 randCircle = random.NextFloat2Direction() * random.NextFloat(0, range);

        quaternion alignmentRot = quaternion.LookRotationSafe(math.cross(up, math.right()), up);

        float3 localOffset = new float3(randCircle.x, 0f, randCircle.y);
        float3 worldOffset = math.rotate(alignmentRot, localOffset);

        float3 roughPosition = centerPos + worldOffset;

        var success = SnapToSurfaceRaycast(ref collisionWorld, roughPosition, planetCenter, filter, 50f, out var hit);
        foundPosition = hit.Position;

        return success;
    }

    /// <summary>
    /// Gets the euclidean (straight-line) distance between two points.
    /// </summary>
    [BurstCompile]
    public static void GetDistanceEuclidean(in float3 from, in float3 to, out float distance)
    {
        distance = math.distance(from, to);
    }

    /// <summary>
    /// Checks if a target is within a given euclidean (straight-line) distance.
    /// </summary>
    [BurstCompile]
    public static void IsWithinRangeEuclidean(in float3 center, in float3 target, float range, out bool result)
    {
        result = math.distancesq(center, target) <= range * range;
    }

    #endregion
}