using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Time + kills enemy HP scaling, applied at spawn.
///
///   D    = Timer / SecondsPerUnit + EnemiesKilled / KillsPerUnit   (run "difficulty units")
///   p    = clamp(D / ReferenceUnits, 0, 1)                         (run progress 0..1)
///   HealthMult = 1 + (MaxHealthMult - 1) * p^CurveExponent
///
/// CurveExponent is the SHAPE knob (applied to normalized progress, so it actually pivots the curve):
///   &lt; 1  → front-loaded: harder EARLY, eases off toward the plateau (what we want);
///   = 1  → linear ramp to the plateau;
///   &gt; 1  → back-loaded: easy early, spikes toward the end.
/// MaxHealthMult = late-game plateau (lower = easier endgame). ReferenceUnits = the D at which the
/// plateau is reached (~end of a run). Keyed to run time + kills only (never metaprogression), so a
/// fresh player and a veteran meet identical enemy HP at the same point. Override via
/// <see cref="EnemyScalingConfigAuthoring"/>.
/// </summary>
public struct EnemyScalingConfig : IComponentData
{
    public float SecondsPerUnit;
    public float KillsPerUnit;
    public float ReferenceUnits;
    public float CurveExponent;
    public float MaxHealthMult;

    public static EnemyScalingConfig Default => new EnemyScalingConfig
    {
        SecondsPerUnit = 60f,
        KillsPerUnit = 150f,
        ReferenceUnits = 16f,
        CurveExponent = 0.5f,
        MaxHealthMult = 2.5f
    };

    /// <summary> HP multiplier for the current run progress. Always >= 1. </summary>
    public float ComputeHealthMult(float timer, float enemiesKilled)
    {
        float secs = SecondsPerUnit > 0f ? SecondsPerUnit : 60f;
        float kills = KillsPerUnit > 0f ? KillsPerUnit : 150f;
        float refU = ReferenceUnits > 0f ? ReferenceUnits : 16f;
        float exp = CurveExponent > 0f ? CurveExponent : 1f;
        float max = MaxHealthMult > 1f ? MaxHealthMult : 1f;

        float d = timer / secs + enemiesKilled / kills;
        float p = math.clamp(d / refU, 0f, 1f);
        float mult = 1f + (max - 1f) * math.pow(p, exp);

        return mult < 1f ? 1f : mult;
    }
}
