using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

/// <summary>Surface locomotion shared by enemies (FlowFieldMovementSystem) and Follow Summons (SummonMovementSystem):
/// mass damping, persistent Reynolds velocity, facing, terrain snap. Bodies moved verbatim from FlowFieldMovementSystem
/// (spec allegeance-summon, Impact unknown 3) — a change here changes the enemies.</summary>
public static class SurfaceSteering
{
    // Half-length of the short ground probe (the entity moves at most a couple of units per frame);
    // SnapDistance is the fallback probe when the ground is out of the short ray's reach.
    internal const float GroundProbeDistance = 4f;
    internal const float SnapDistance = 500f;
    internal const float VertSnapSpeed = 20.0f;

    /// <summary>Heavier entities accelerate / turn more slowly; floored at <paramref name="minFactor"/>.</summary>
    public static float MassFactor(float mass, float referenceMass, float influence, float minFactor)
    {
        float massRatio = referenceMass / math.max(0.0001f, mass);
        return math.max(minFactor, math.pow(massRatio, influence));
    }

    /// <summary>Accelerates a persistent velocity toward the desired one (re-projected tangent first): low-passes jitter.</summary>
    public static float3 RampVelocity(float3 velocity, float3 normal, float3 desiredVelocity, float acceleration, float deltaTime)
    {
        // The surface normal rotates as the entity travels, so re-project the carried velocity tangent.
        float3 carried = velocity - normal * math.dot(velocity, normal);

        float3 deltaV = desiredVelocity - carried;
        float deltaVLen = math.length(deltaV);
        float maxDeltaV = acceleration * deltaTime;
        return deltaVLen > maxDeltaV && deltaVLen > 1e-6f
            ? carried + deltaV * (maxDeltaV / deltaVLen)
            : desiredVelocity;
    }

    /// <summary>Direction of travel; below the speed threshold, the fallback (a near-zero velocity has no direction).</summary>
    public static float3 FaceDirection(float3 velocity, float speedThreshold, float3 fallback)
    {
        float currentSpeed = math.length(velocity);
        return currentSpeed > speedThreshold ? velocity / currentSpeed : fallback;
    }

    /// <summary>Terrain snap: short probe first, long ray only when it misses (cliff edge, spawn, teleport); full
    /// horizontal move, smoothed vertical; turn at most <paramref name="maxTurnRadians"/> toward the face direction.</summary>
    public static void SnapToGroundAndTurn(ref LocalTransform transform, in CollisionWorld collisionWorld,
        float3 desiredPosition, float3 currentNormal, float3 faceDirection, float maxTurnRadians, float deltaTime)
    {
        var input = new RaycastInput
        {
            Start = desiredPosition + currentNormal * GroundProbeDistance,
            End = desiredPosition - currentNormal * GroundProbeDistance,
            Filter = new CollisionFilter
            {
                BelongsTo = CollisionLayers.Raycast,
                CollidesWith = CollisionLayers.Landscape
            }
        };

        bool grounded = collisionWorld.CastRay(input, out var hit);
        if (!grounded)
        {
            input.Start = desiredPosition + currentNormal * SnapDistance;
            input.End = desiredPosition - currentNormal * SnapDistance;
            grounded = collisionWorld.CastRay(input, out hit);
        }

        if (grounded)
        {
            // Decompose the delta into horizontal (tangential) and vertical (normal) parts.
            // Apply full horizontal movement; smooth only the vertical (terrain-following) component
            // to avoid jarring jumps over bumpy terrain.
            float3 toHit = hit.Position - transform.Position;
            float verticalDelta = math.dot(toHit, currentNormal);
            float3 horizontalDelta = toHit - currentNormal * verticalDelta;

            // Snap immediately for large gaps (spawning/teleport), smooth for small terrain bumps
            float absVert = math.abs(verticalDelta);
            float vertSmooth = absVert > 1.5f
                ? verticalDelta
                : verticalDelta * math.min(1f, deltaTime * VertSnapSpeed);

            transform.Position += horizontalDelta + currentNormal * vertSmooth;

            PlanetUtils.GetRotationOnSurface(in faceDirection, hit.SurfaceNormal, out quaternion targetRotation);
            transform.Rotation = RotationMath.RotateTowards(transform.Rotation, targetRotation, maxTurnRadians);
        }
        else
        {
            transform.Position = desiredPosition;

            // Keep turning even when off the navigable mesh.
            PlanetUtils.GetRotationOnSurface(in faceDirection, currentNormal, out quaternion targetRotation);
            transform.Rotation = RotationMath.RotateTowards(transform.Rotation, targetRotation, maxTurnRadians);
        }
    }

    /// <summary>Beyond the camera horizon: snap to the perfect sphere with no raycast, then turn.</summary>
    public static void SnapToSphereAndTurn(ref LocalTransform transform, float3 desiredPosition, float3 planetCenter,
        float planetRadius, float3 faceDirection, float maxTurnRadians)
    {
        PlanetUtils.SnapToSurfaceRadius(in desiredPosition, in planetCenter, planetRadius, out float3 snapped);
        transform.Position = snapped;

        PlanetUtils.GetSurfaceNormalRadius(in snapped, in planetCenter, out float3 sphereNormal);
        PlanetUtils.GetRotationOnSurface(in faceDirection, sphereNormal, out quaternion sphereRotation);
        transform.Rotation = RotationMath.RotateTowards(transform.Rotation, sphereRotation, maxTurnRadians);
    }
}
