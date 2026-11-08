using Unity.Mathematics;

/// <summary>Pure rotation arithmetic (no ECS state), Burst-compatible. Shared by FlowFieldMovementSystem (enemies)
/// and BeamAimSystem (channeled beam).</summary>
public static class RotationMath
{
    /// <summary>
    /// Rotates <paramref name="from"/> toward <paramref name="to"/> by at most
    /// <paramref name="maxRadians"/> — a hard cap on angular speed (degrees/second).
    /// </summary>
    public static quaternion RotateTowards(quaternion from, quaternion to, float maxRadians)
    {
        // Angle between two unit quaternions; abs() picks the shorter of the two equivalent arcs.
        float dot = math.clamp(math.abs(math.dot(from.value, to.value)), -1f, 1f);
        float angle = 2f * math.acos(dot);

        if (angle <= maxRadians || angle < 1e-5f)
            return to;

        return math.slerp(from, to, maxRadians / angle);
    }
}
