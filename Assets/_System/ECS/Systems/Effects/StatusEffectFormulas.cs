public static class StatusEffectFormulas
{
    /// <summary>Burn: ratio × the triggering hit's pre-crit damage. The crit roll of the hit that applies
    /// the Burn never affects the Burn's own magnitude — matches current behavior, confirmed by design.</summary>
    public static float ComputeBurnMagnitude(float ratio, float preCritHitDamage) => ratio * preCritHitDamage;

    /// <summary>Slow/Stun/Knockback: flat, config-driven — no natural quantity on the hit to derive from.</summary>
    public static float ComputeFlatMagnitude(float baseMagnitude) => baseMagnitude;
}
