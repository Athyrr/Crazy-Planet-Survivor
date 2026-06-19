using Unity.Entities;

/// <summary>
/// Tunables for the time + kills enemy HP scaling (linear, applied at spawn).
///
/// Difficulty units:   D = Timer / SecondsPerUnit + EnemiesKilled / KillsPerUnit
/// Health multiplier:  HealthMult = clamp(1 + D * HealthGrowthPerUnit, 1, MaxHealthMult)
///
/// Keyed purely to run time + kills (never to metaprogression), so a fresh player and a veteran
/// meet identical enemy HP at the same point in a run. Place an <see cref="EnemyScalingConfigAuthoring"/>
/// in a baked subscene to override the built-in <see cref="Default"/>.
/// </summary>
public struct EnemyScalingConfig : IComponentData
{
    public float SecondsPerUnit;
    public float KillsPerUnit;
    public float HealthGrowthPerUnit;
    public float MaxHealthMult;

    public static EnemyScalingConfig Default => new EnemyScalingConfig
    {
        SecondsPerUnit = 60f,
        KillsPerUnit = 100f,
        HealthGrowthPerUnit = 0.5f,
        MaxHealthMult = 20f
    };

    /// <summary> Linear HP multiplier for the current run progress. Always >= 1. </summary>
    public float ComputeHealthMult(float timer, float enemiesKilled)
    {
        float secs = SecondsPerUnit > 0f ? SecondsPerUnit : 60f;
        float kills = KillsPerUnit > 0f ? KillsPerUnit : 100f;

        float d = timer / secs + enemiesKilled / kills;
        float mult = 1f + d * HealthGrowthPerUnit;

        if (MaxHealthMult > 1f && mult > MaxHealthMult)
            mult = MaxHealthMult;

        return mult < 1f ? 1f : mult;
    }
}
