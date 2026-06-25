using Unity.Entities;

/// <summary>
/// Tunables for the run difficulty scaling (linear, keyed to run time + kills):
///   - enemy HP multiplier (applied at spawn by <see cref="EnemiesSpawnerSystem"/>)
///   - player armor penetration (applied to incoming damage by <see cref="HealthSystem"/>)
///
/// Difficulty units:   D = Timer / SecondsPerUnit + EnemiesKilled / KillsPerUnit
/// Health multiplier:  HealthMult  = clamp(1 + D * HealthGrowthPerUnit, 1, MaxHealthMult)
/// Damage multiplier:  DamageMult  = clamp(1 + D * DamageGrowthPerUnit, 1, MaxDamageMult)
/// Armor penetration:  ArmorPen    = clamp(D * ArmorPenPerUnit, 0, MaxArmorPen)
///
/// Enemy HP and outgoing damage both grow with the run (HP applied at spawn, damage applied at spawn
/// for contact and at cast for enemy spells). Player armor is a FLAT stat: a hit deals
/// max(MinDamagePerHit, rawHit - max(0, Armor - ArmorPen)). Because ArmorPen grows over the run, a
/// fixed amount of armor mitigates less and less — strong early, negligible late unless the player
/// keeps investing into armor.
///
/// Keyed purely to run time + kills (never to metaprogression), so a fresh player and a veteran
/// meet identical scaling at the same point in a run. Place an <see cref="EnemyScalingConfigAuthoring"/>
/// in a baked subscene to override the built-in <see cref="Default"/>.
/// </summary>
public struct EnemyScalingConfig : IComponentData
{
    public float SecondsPerUnit;
    public float KillsPerUnit;

    public float HealthGrowthPerUnit;
    public float MaxHealthMult;

    public float DamageGrowthPerUnit;   // enemy outgoing damage added per difficulty unit (0.3 = +30%/unit)
    public float MaxDamageMult;         // cap on the enemy damage multiplier

    public float ArmorPenPerUnit;   // flat player armor neutralized per difficulty unit
    public float MaxArmorPen;       // cap on armor penetration (0 = uncapped)
    public float MinDamagePerHit;   // damage floor per hit after armor mitigation

    public static EnemyScalingConfig Default => new EnemyScalingConfig
    {
        SecondsPerUnit = 60f,
        KillsPerUnit = 100f,
        HealthGrowthPerUnit = 0.5f,
        MaxHealthMult = 20f,
        DamageGrowthPerUnit = 0.3f,
        MaxDamageMult = 10f,
        ArmorPenPerUnit = 0f,   // off by default: DamageMult already erodes flat armor's relevance (avoids double scaling)
        MaxArmorPen = 0f,
        MinDamagePerHit = 1f
    };

    /// <summary> Difficulty units for the current run progress (time + kills). </summary>
    public float ComputeDifficultyUnits(float timer, float enemiesKilled)
    {
        float secs = SecondsPerUnit > 0f ? SecondsPerUnit : 60f;
        float kills = KillsPerUnit > 0f ? KillsPerUnit : 100f;
        return timer / secs + enemiesKilled / kills;
    }

    /// <summary> Linear HP multiplier for the current run progress. Always >= 1. </summary>
    public float ComputeHealthMult(float timer, float enemiesKilled)
    {
        float d = ComputeDifficultyUnits(timer, enemiesKilled);
        float mult = 1f + d * HealthGrowthPerUnit;

        if (MaxHealthMult > 1f && mult > MaxHealthMult)
            mult = MaxHealthMult;

        return mult < 1f ? 1f : mult;
    }

    /// <summary> Linear enemy outgoing-damage multiplier for the current run progress. Always >= 1. </summary>
    public float ComputeDamageMult(float timer, float enemiesKilled)
    {
        float d = ComputeDifficultyUnits(timer, enemiesKilled);
        float mult = 1f + d * DamageGrowthPerUnit;

        if (MaxDamageMult > 1f && mult > MaxDamageMult)
            mult = MaxDamageMult;

        return mult < 1f ? 1f : mult;
    }

    /// <summary> Flat player armor neutralized at the current run progress. Always >= 0. </summary>
    public float ComputeArmorPenetration(float timer, float enemiesKilled)
    {
        float d = ComputeDifficultyUnits(timer, enemiesKilled);
        float pen = d * ArmorPenPerUnit;

        if (MaxArmorPen > 0f && pen > MaxArmorPen)
            pen = MaxArmorPen;

        return pen < 0f ? 0f : pen;
    }
}
