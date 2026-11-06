using Unity.Entities;

/// <summary>How ResolveHit rolls crit for a hit. Roll MUST stay 0: HitSource is built with object
/// initializers in several places, an unset field must keep the old behavior.</summary>
public enum ECritMode : byte { Roll = 0, ForceCrit = 1, ForceNoCrit = 2 }

/// <summary>Hit rules of an emitted child, copied into HitSource and read once in ResolveHit.</summary>
public struct EmittedHitRules : IComponentData
{
    public ECritMode CritMode;
    /// <summary>Explosion life-steal rule: blocked unless EmitterConfig.ExplosionAllowLifeSteal or the caster's
    /// CoreStats.ExplosionCanLifeSteal.</summary>
    public bool LifeStealBlocked;
}
