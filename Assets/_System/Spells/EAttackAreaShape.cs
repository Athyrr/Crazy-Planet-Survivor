public enum EAttackAreaShape
{
    Circle = 0,
    Cone = 1,
    Ring = 2,

    /// <summary>Line/thrust (estoc, lance, beam). Spans the entity origin → its Offset endpoint;
    /// half-width = RadiusStart (set RadiusStart == RadiusEnd). Points toward the target.</summary>
    Capsule = 3,
}
