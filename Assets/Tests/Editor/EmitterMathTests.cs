using NUnit.Framework;
using Unity.Mathematics;

/// <summary>Pins the pure emitter arithmetic (spec generalized-emitter, Play scenarios 3/4: 60 % then 36 %).</summary>
public class EmitterMathTests
{
    [Test] public void Falloff_Generation0_IsOne() => Assert.AreEqual(1f, EmitterMath.Falloff(0.6f, 0), 1e-6f);
    [Test] public void Falloff_Generation1_Is60Percent() => Assert.AreEqual(0.6f, EmitterMath.Falloff(0.6f, 1), 1e-6f);
    [Test] public void Falloff_Generation2_Is36Percent() => Assert.AreEqual(0.36f, EmitterMath.Falloff(0.6f, 2), 1e-6f);

    [Test] public void EaseLinear_IsIdentity() => Assert.AreEqual(0.3f, EmitterMath.EaseRadiusT(ERadiusEase.Linear, 0.3f), 1e-6f);
    [Test] public void EaseOutCubic_Half() => Assert.AreEqual(0.875f, EmitterMath.EaseRadiusT(ERadiusEase.EaseOutCubic, 0.5f), 1e-6f);
    [Test] public void EaseOutQuad_Half() => Assert.AreEqual(0.75f, EmitterMath.EaseRadiusT(ERadiusEase.EaseOutQuad, 0.5f), 1e-6f);
    [Test] public void Ease_ClampsAndHitsEndpoints()
    {
        Assert.AreEqual(0f, EmitterMath.EaseRadiusT(ERadiusEase.EaseOutCubic, -1f), 1e-6f);
        Assert.AreEqual(1f, EmitterMath.EaseRadiusT(ERadiusEase.EaseOutCubic, 2f), 1e-6f);
    }

    [Test] public void ExplosionScale_NoBonus_IsOne() => Assert.AreEqual(1f, EmitterMath.ExplosionScale(0f), 1e-6f);
    [Test] public void ExplosionScale_Bonus() => Assert.AreEqual(1.25f, EmitterMath.ExplosionScale(0.25f), 1e-6f);
    [Test] public void ExplosionScale_NeverBelowFloor() => Assert.AreEqual(0.1f, EmitterMath.ExplosionScale(-5f), 1e-6f);

    [Test] public void Spread_Single_IsZero() => Assert.AreEqual(0f, EmitterMath.SpreadAngleDegrees(0, 1, 30f), 1e-6f);
    [Test] public void Spread_Two_IsCentered()
    {
        Assert.AreEqual(-15f, EmitterMath.SpreadAngleDegrees(0, 2, 30f), 1e-5f);
        Assert.AreEqual(15f, EmitterMath.SpreadAngleDegrees(1, 2, 30f), 1e-5f);
    }
    [Test] public void Spread_Three_HasMiddleAtZero() => Assert.AreEqual(0f, EmitterMath.SpreadAngleDegrees(1, 3, 30f), 1e-5f);

    [Test] public void Circle_QuarterStep() => Assert.AreEqual(math.PI * 0.5f, EmitterMath.CircleAngleRadians(0f, 1, 4), 1e-5f);

    [Test] public void OrbitRadius_NoRangeBonus_KeepsEntryRadius() => Assert.AreEqual(8f, EmitterMath.OrbitRadius(8f, 1f, 1f), 1e-5f);
    [Test] public void OrbitRadius_RangeBonus_Scales() => Assert.AreEqual(12f, EmitterMath.OrbitRadius(8f, 1.5f, 1f), 1e-5f);

    [Test] public void TangentReference_IsUnitAndPerpendicular()
    {
        float3 n = math.normalize(new float3(0.2f, 1f, 0.1f));
        float3 t = EmitterMath.TangentReference(n, new float3(0f, 0f, 1f));
        Assert.AreEqual(1f, math.length(t), 1e-5f);
        Assert.AreEqual(0f, math.dot(t, n), 1e-5f);
    }
    [Test] public void TangentReference_HintParallelToNormal_StillPerpendicular()
    {
        float3 n = new float3(0f, 1f, 0f);
        float3 t = EmitterMath.TangentReference(n, n);
        Assert.AreEqual(0f, math.dot(t, n), 1e-5f);
        Assert.AreEqual(1f, math.length(t), 1e-5f);
    }

    [Test] public void OrbitOffset_AngleZero_AlongReference()
    {
        float3 o = EmitterMath.OrbitOffset(new float3(0f, 1f, 0f), new float3(0f, 0f, 1f), 8f, 0f);
        Assert.AreEqual(0f, math.distance(o, new float3(0f, 0f, 8f)), 1e-4f);
    }
    [Test] public void OrbitOffset_QuarterTurn_AlongNormalCrossReference()
    {
        float3 o = EmitterMath.OrbitOffset(new float3(0f, 1f, 0f), new float3(0f, 0f, 1f), 8f, math.PI * 0.5f);
        Assert.AreEqual(0f, math.distance(o, new float3(8f, 0f, 0f)), 1e-4f);
    }

    [Test] public void ChanceRoll_Always() => Assert.IsTrue(EmitterMath.ChanceRoll(0.999f, 1f));
    [Test] public void ChanceRoll_Never() => Assert.IsFalse(EmitterMath.ChanceRoll(0f, 0f));
    [Test] public void ChanceRoll_Threshold()
    {
        Assert.IsTrue(EmitterMath.ChanceRoll(0.2f, 0.3f));
        Assert.IsFalse(EmitterMath.ChanceRoll(0.5f, 0.3f));
    }

    [Test] public void Budget_Truncates() => Assert.AreEqual(3, EmitterMath.ClampToBudget(5, 3));
    [Test] public void Budget_Exhausted_IsZero() => Assert.AreEqual(0, EmitterMath.ClampToBudget(5, -2));
}
