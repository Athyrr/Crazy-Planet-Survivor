using Unity.Collections;
using Unity.Mathematics;

/// <summary>Per-stat inheritance ratio of a Summon (spec allegeance-summon §2): InheritRatio everywhere, then the SummonSO
/// overrides (defaults: LifeStealChance and CritChance at 0).</summary>
public struct SummonInheritRatios
{
    // Delta stats (neutral 0)
    public float Damage, AttackSpeed, SpellSize, SpellSpeed, SpellDuration, CastRange, MoveSpeed;
    // Absolute additive stats
    public float CritChance, CritDamage, LifeStealChance;

    public static SummonInheritRatios Uniform(float ratio) => new SummonInheritRatios
    {
        Damage = ratio, AttackSpeed = ratio, SpellSize = ratio, SpellSpeed = ratio, SpellDuration = ratio,
        CastRange = ratio, MoveSpeed = ratio, CritChance = ratio, CritDamage = ratio, LifeStealChance = ratio,
    };

    /// <summary>False for a stat a Summon does not inherit (the caller warns).</summary>
    public bool TrySet(ECharacterStat stat, float ratio)
    {
        switch (stat)
        {
            case ECharacterStat.Damage: Damage = ratio; return true;
            case ECharacterStat.AttackSpeed: AttackSpeed = ratio; return true;
            case ECharacterStat.SizeMultiplier: SpellSize = ratio; return true;
            case ECharacterStat.SpellSpeed: SpellSpeed = ratio; return true;
            case ECharacterStat.SpellDuration: SpellDuration = ratio; return true;
            case ECharacterStat.CastRange: CastRange = ratio; return true;
            case ECharacterStat.Speed: MoveSpeed = ratio; return true;
            case ECharacterStat.CritChance: CritChance = ratio; return true;
            case ECharacterStat.CritDamage: CritDamage = ratio; return true;
            case ECharacterStat.LifeStealChance: LifeStealChance = ratio; return true;
            default: return false;
        }
    }
}

/// <summary>Pure Summon arithmetic (no ECS state), Burst-compatible: inheritance, ring placement, leash.</summary>
public static class SummonMath
{
    private const float TwoPi = 2f * math.PI;

    /// <summary>Delta stat (neutral 0): (1 + base)(1 + ratio × owner) − 1.</summary>
    public static float InheritDelta(float baseValue, float ratio, float ownerValue)
        => (1f + baseValue) * (1f + ratio * ownerValue) - 1f;

    /// <summary>Absolute additive stat (crit chance, crit damage, life steal): base + ratio × owner.</summary>
    public static float InheritAdditive(float baseValue, float ratio, float ownerValue)
        => baseValue + ratio * ownerValue;

    /// <summary>Agent CoreStats = its base (absolute stats kept: MaxHealth, BaseMoveSpeed…) + the owner's inherited
    /// bonuses; Damage is further multiplied by (1 + owner SummonPower). Every other stat keeps the base value (0 =
    /// neutral): Amount / Pierce / Bounce, status-effect multipliers, armor… are never inherited.</summary>
    public static CoreStats ComposeAgentCoreStats(in CoreStats baseStats, in SummonInheritRatios ratios, in CoreStats owner)
    {
        CoreStats s = baseStats;
        s.Damage = (1f + InheritDelta(baseStats.Damage, ratios.Damage, owner.Damage)) * (1f + owner.SummonPower) - 1f;
        s.AttackSpeed = InheritDelta(baseStats.AttackSpeed, ratios.AttackSpeed, owner.AttackSpeed);
        s.SpellSize = InheritDelta(baseStats.SpellSize, ratios.SpellSize, owner.SpellSize);
        s.SpellSpeed = InheritDelta(baseStats.SpellSpeed, ratios.SpellSpeed, owner.SpellSpeed);
        s.SpellDuration = InheritDelta(baseStats.SpellDuration, ratios.SpellDuration, owner.SpellDuration);
        s.CastRange = InheritDelta(baseStats.CastRange, ratios.CastRange, owner.CastRange);
        s.MoveSpeed = InheritDelta(baseStats.MoveSpeed, ratios.MoveSpeed, owner.MoveSpeed);
        s.CritChance = InheritAdditive(baseStats.CritChance, ratios.CritChance, owner.CritChance);
        s.CritDamage = InheritAdditive(baseStats.CritDamage, ratios.CritDamage, owner.CritDamage);
        s.LifeStealChance = InheritAdditive(baseStats.LifeStealChance, ratios.LifeStealChance, owner.LifeStealChance);
        return s;
    }

    /// <summary>Angle of an offset around the owner, in the frame of EmitterMath.OrbitOffset (c·reference + s·side).</summary>
    public static float RingAngle(float3 offset, float3 normal, float3 reference)
    {
        float3 side = math.cross(normal, reference);
        return math.atan2(math.dot(offset, side), math.dot(offset, reference));
    }

    /// <summary>Middle of the largest free arc between the occupied angles (radians); 0 when the ring is empty.
    /// Ties keep the first gap in ascending angle order.</summary>
    public static float FreeRingAngle(NativeArray<float> occupiedRadians)
    {
        int n = occupiedRadians.Length;
        if (n == 0)
            return 0f;

        var sorted = new NativeArray<float>(n, Allocator.Temp);
        for (int i = 0; i < n; i++)
            sorted[i] = Wrap(occupiedRadians[i]);
        sorted.Sort();

        float bestGap = -1f, bestMiddle = 0f;
        for (int i = 0; i < n; i++)
        {
            float from = sorted[i];
            float to = i + 1 < n ? sorted[i + 1] : sorted[0] + TwoPi;
            float gap = to - from;
            if (gap > bestGap + 1e-5f)
            {
                bestGap = gap;
                bestMiddle = from + gap * 0.5f;
            }
        }
        sorted.Dispose();
        return Wrap(bestMiddle);
    }

    /// <summary>Distance along the planet surface (great circle at a's radius); straight distance without a planet.</summary>
    public static float ArcDistance(float3 a, float3 b, float3 center)
    {
        float3 da = a - center;
        float r = math.length(da);
        if (r < 1e-4f)
            return math.distance(a, b);
        float3 na = da / r;
        float cos = math.clamp(math.dot(na, math.normalizesafe(b - center, na)), -1f, 1f);
        return r * math.acos(cos);
    }

    /// <summary>Leash with hysteresis (spec §3): starts returning beyond leashRadius, stops once back within
    /// leashRadius × returnFraction.</summary>
    public static bool UpdateLeash(bool returning, float distanceToOwner, float leashRadius, float returnFraction)
        => returning ? distanceToOwner > leashRadius * returnFraction : distanceToOwner > leashRadius;

    /// <summary>Speed factor toward a goal: 0 within stopDistance, easing in over half of it beyond; 1 when stopDistance ≤ 0.</summary>
    public static float ApproachFactor(float distance, float stopDistance)
    {
        if (stopDistance <= 0f)
            return 1f;
        if (distance <= stopDistance)
            return 0f;
        return math.saturate((distance - stopDistance) / (stopDistance * 0.5f));
    }

    private static float Wrap(float radians) => radians - TwoPi * math.floor(radians / TwoPi);
}
