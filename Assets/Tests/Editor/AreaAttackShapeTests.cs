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
    public void Cone_RandomSampling_MatchesHandComputedDotProduct()
    {
        // N random points, compared against the same dot-product formula IsInShape uses internally —
        // this is the actual desync guard: if a future edit changes the cone's rotation axis or comparison
        // operator, this test catches it even without a screenshot review.
        quaternion rot = quaternion.AxisAngle(new float3(0f, 1f, 0f), math.radians(30f));
        float halfAngle = math.radians(35f);
        var rng = Rng;

        for (int i = 0; i < 200; i++)
        {
            float3 hitPos = rng.NextFloat3Direction() * rng.NextFloat(0.1f, 10f);

            float3 fwd = math.forward(rot);
            float3 upAxis = math.mul(rot, math.up());
            float3 coneDir = math.normalize(math.mul(quaternion.AxisAngle(upAxis, 0f), fwd));
            float3 toHit = math.normalize(hitPos - float3.zero);
            bool expected = math.dot(coneDir, toHit) >= math.cos(halfAngle);

            bool actual = AreaAttackSystem.IsInShape(EAttackAreaShape.Cone, float3.zero, rot, 100f, halfAngle, 0f, 0f, hitPos);

            Assert.AreEqual(expected, actual, $"Mismatch at sample {i}, hitPos={hitPos}");
        }
    }
}
