/// <summary>
/// Category of an incoming damage instance, used to scale the camera shake intensity in
/// <see cref="ShakeFeedbackComponent"/> via the CameraShakeSettings SO.
///
/// Values are ordered by intensity/priority: when several damage instances hit the player in the
/// same frame, <see cref="HealthSystem"/> keeps the one with the highest numeric value
/// (see the byte comparison there). Re-order the values to change the priority.
/// </summary>
public enum EDamageShakeSource : byte
{
    None = 0,       // unknown / unstamped — falls back to the SO Default profile (also used by player death)
    DoT = 1,        // burn ticks, tick zones, environmental hazard (lava) — lightest
    Enemy = 2,      // normal enemy: contact, projectile or area attack
    Explosion = 3,  // explosion AoE (Explosive tag)
    Elite = 4,      // elite (mini-boss)
    Boss = 5,       // final boss — strongest
}
