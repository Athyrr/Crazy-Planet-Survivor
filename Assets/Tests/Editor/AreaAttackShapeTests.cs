using NUnit.Framework;
using Unity.Mathematics;

public class AreaAttackShapeTests
{
    private static readonly Unity.Mathematics.Random Rng = Unity.Mathematics.Random.CreateFromIndex(12345);

    [Test]
    public void Circle_AlwaysInShape()
    {
        Assert.IsTrue(AreaAttackSystem.IsInShape(EAttackAreaShape.Circle, float3.zero, quaternion.identity,
            radius: 5f, halfAngle: 0f, sweep: 0f, ringThickness: 0f, hitPos: new float3(4.9f, 0f, 0f)));
    }

    [Test]
    public void Cone_PointsAlongForward_HitsInFrontAndMissesBehind()
    {
        quaternion rot = quaternion.identity; // forward = +Z
        float radius = 5f, halfAngle = math.radians(45f);

        bool inFront = AreaAttackSystem.IsInShape(EAttackAreaShape.Cone, float3.zero, rot, radius, halfAngle, 0f, 0f,
            hitPos: new float3(0f, 0f, 3f));
        bool behind = AreaAttackSystem.IsInShape(EAttackAreaShape.Cone, float3.zero, rot, radius, halfAngle, 0f, 0f,
            hitPos: new float3(0f, 0f, -3f));

        Assert.IsTrue(inFront, "A point straight ahead, inside the half-angle, must be in the cone.");
        Assert.IsFalse(behind, "A point directly behind the apex must never be in a forward-facing cone.");
    }

    [Test]
    public void Ring_OnlyMatchesTheBand()
    {
        float radius = 5f, thickness = 1f;

        bool onBand = AreaAttackSystem.IsInShape(EAttackAreaShape.Ring, float3.zero, quaternion.identity,
            radius, 0f, 0f, thickness, hitPos: new float3(5f, 0f, 0f));
        bool insideHole = AreaAttackSystem.IsInShape(EAttackAreaShape.Ring, float3.zero, quaternion.identity,
            radius, 0f, 0f, thickness, hitPos: new float3(1f, 0f, 0f));
        bool outsideRing = AreaAttackSystem.IsInShape(EAttackAreaShape.Ring, float3.zero, quaternion.identity,
            radius, 0f, 0f, thickness, hitPos: new float3(20f, 0f, 0f));

        Assert.IsTrue(onBand, "A point on the ring's radius must be in the band.");
        Assert.IsFalse(insideHole, "A point well inside the ring's hole must not match.");
        Assert.IsFalse(outsideRing, "A point well outside the ring's outer edge must not match.");
    }

    [Test]
    public void Cone_RandomSampling_MatchesIndependentlyDerivedExpectation()
    {
        // I3 fix (final whole-branch review): the old version of this test used sweep: 0f and recomputed
        // IsInShape's own expression verbatim for `expected` — AxisAngle(anyAxis, 0) is the identity
        // rotation, and with a rotation that is itself purely a spin around world Y, the entity's local up
        // (rot * world-up) is world Y too, so a regression that swept the cone around world Y instead of
        // the entity's local up (spec §4.7's historical bug) would multiply out of both sides and pass all
        // 200 samples regardless. Two changes close that: (1) `rot` now tilts on X too, so local up and
        // world Y genuinely diverge — a world-Y-sweep bug and a local-up-sweep are no longer the same
        // rotation; (2) `sweep` is non-zero, so the sweep axis is actually exercised; (3) `expected` is
        // derived independently — UnityEngine.Quaternion/Vector3 (a different math library than
        // IsInShape's Unity.Mathematics quaternion/math.mul) and Vector3.Angle (a different comparison than
        // IsInShape's dot/cos), built the same way AreaAttackAuthoring.cs's gizmo (DrawConeShape:
        // `Quaternion.AngleAxis(sweepDeg, up) * fwd`) actually draws the cone — not a copy of IsInShape's
        // own formula.
        quaternion rot = math.mul(
            quaternion.AxisAngle(new float3(1f, 0f, 0f), math.radians(40f)),
            quaternion.AxisAngle(new float3(0f, 1f, 0f), math.radians(30f)));
        float halfAngleDeg = 35f;
        float halfAngle = math.radians(halfAngleDeg);
        float sweepDeg = 20f;
        float sweep = math.radians(sweepDeg);
        var rng = Rng;

        // Independently-derived expected cone axis: UnityEngine types/APIs throughout, not
        // Unity.Mathematics — a genuinely different implementation of the same rotate-forward-around-
        // local-up-by-sweep operation, so it can't share a bug with IsInShape's own formula.
        var urot = new UnityEngine.Quaternion(rot.value.x, rot.value.y, rot.value.z, rot.value.w);
        UnityEngine.Vector3 localUp = urot * UnityEngine.Vector3.up;
        UnityEngine.Vector3 localFwd = urot * UnityEngine.Vector3.forward;
        UnityEngine.Vector3 expectedConeDir = UnityEngine.Quaternion.AngleAxis(sweepDeg, localUp) * localFwd;

        for (int i = 0; i < 200; i++)
        {
            float3 hitPos = rng.NextFloat3Direction() * rng.NextFloat(0.1f, 10f);

            var toHit = new UnityEngine.Vector3(hitPos.x, hitPos.y, hitPos.z);
            bool expected = UnityEngine.Vector3.Angle(expectedConeDir, toHit) <= halfAngleDeg;

            bool actual = AreaAttackSystem.IsInShape(EAttackAreaShape.Cone, float3.zero, rot, 100f, halfAngle, sweep, 0f, hitPos);

            Assert.AreEqual(expected, actual, $"Mismatch at sample {i}, hitPos={hitPos}");
        }
    }
}
