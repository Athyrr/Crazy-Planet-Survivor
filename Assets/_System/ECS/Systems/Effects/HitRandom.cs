using Unity.Entities;
using Unity.Mathematics;

/// <summary>Single seed formula for every hit-resolution RNG stream (crit rolls, life-steal, burn-tick
/// crit). Replaces 3 divergent formulas: CollisionSystem's (correct — this one), AreaAttackSystem's Burst
/// cadence (bug: ignored frameSeed entirely, `Random.CreateFromIndex((uint)(entity.Index + 1))` —
/// correlated crit across frames for the same entity), and AreaAttackSystem's OverTime cadence (its own
/// third variant). entityB defaults to Entity.Null for single-entity call sites.</summary>
public static class HitRandom
{
    public static Random CreateForHit(uint frameSeed, Entity entityA, Entity entityB = default)
    {
        uint mixed = (frameSeed ^ ((uint)entityA.Index * 0x9E3779B1u) ^ ((uint)entityB.Index * 0x85EBCA77u)) | 1u;
        return Random.CreateFromIndex(mixed);
    }
}
