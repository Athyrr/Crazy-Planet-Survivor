/// <summary>Opt-in easing of an AreaAttack Burst radius over its ActiveDuration (default Linear keeps every
/// existing Burst spell unchanged). Quad vs cubic is tuned at Play (open question of the spec).</summary>
public enum ERadiusEase : byte { Linear = 0, EaseOutQuad = 1, EaseOutCubic = 2 }
