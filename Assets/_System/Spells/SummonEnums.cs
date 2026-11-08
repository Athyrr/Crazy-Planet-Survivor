/// <summary>Summon axes (SPELL_TAXONOMY §8, spec allegeance-summon §2). All modelled; coded in this version:
/// Mobility Fixed/Follow, Offense Cast, Targeting Nearest, EndOfLife Relocate/Permanent, no aggro.</summary>
public enum ESummonArchetype : byte { Custom, Turret, Pet }

public enum ESummonMobility : byte { Fixed, Follow, Orbit, Roam, Leashed }

public enum ESummonOffense : byte { Cast, Contact, Both }

public enum ESummonTargeting : byte { Nearest, OwnerTarget, None }

public enum ESummonEndOfLife : byte { Relocate, Permanent, DeathCooldown }
