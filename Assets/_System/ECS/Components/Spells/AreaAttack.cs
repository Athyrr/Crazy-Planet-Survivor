using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Component for one-shot area attack spells (VoidSlash, Shockwave, ShockStrike, etc.).
/// Shape parameters are interpolated linearly over ActiveDuration.
/// Uses HitEntityMemory buffer to prevent re-hitting the same entity.
/// </summary>
public struct AreaAttack : IComponentData
{
    public EAttackAreaShape Shape;
    
    public float RadiusStart;       // All shapes: initial radius
    public float RadiusEnd;         // All shapes: final radius (expanding/shrinking)

    public float HalfAngle;         // Cone: aperture half-angle in radians (e.g., 45° = 90° cone)
    public float SweepStart;        // Cone: initial center rotation relative to forward (radians)
    public float SweepEnd;          // Cone: final center rotation relative to forward (radians)

    public float RingThickness;     // Ring: width of the ring band

    public float ActivationDelay;   // Seconds before first evaluation
    public float ActiveDuration;    // How long collision evaluation runs
    public float ElapsedTime;       // Internal timer
    
    public float Damage;
    public float CritChance;
    public float CritMultiplier;
    public Entity Caster;
    public uint TargetLayers;
    public ESpellTag Tags;
}
