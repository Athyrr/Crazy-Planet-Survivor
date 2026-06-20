using Unity.Entities;
using Unity.Mathematics;

/// <summary>Geometry used by a <see cref="HazardZone"/> to test whether an entity is inside.</summary>
public enum EHazardShape : byte
{
    Sphere,
    Box,
}

/// <summary>
/// Effect a hazard zone applies to entities standing inside it. Maps to an existing status-effect
/// component. Extend as new zone effects are needed (the system has one apply branch per type).
/// </summary>
public enum EHazardEffectType : byte
{
    Burn,
    Slow, // todo not applied yet 
    Stun, // todo not applied yet
}

/// <summary>
/// Environmental hazard zone (e.g. lava). While a target-layer entity is inside the shape, the
/// <see cref="HazardZoneSystem"/> applies/refreshes the effects listed in the entity's
/// <see cref="HazardZoneEffectElement"/> buffer.
/// </summary>
public struct HazardZone : IComponentData
{
    public EHazardShape Shape;

    /// <summary>Sphere radius (Shape == Sphere).</summary>
    public float Radius;

    /// <summary>Box half-extents in world axes (Shape == Box, axis-aligned).</summary>
    public float3 BoxHalfExtents;

    /// <summary>Layers whose entities are affected (e.g. CollisionLayers.Player | CollisionLayers.Enemy).</summary>
    public uint TargetLayers;

    /// <summary>
    /// Seconds between effect refreshes. The zone re-applies its effects at this cadence instead of
    /// every frame. Keep every effect's <see cref="HazardZoneEffectElement.Linger"/> &gt;= this value,
    /// otherwise the effect flickers off between refreshes.
    /// </summary>
    public float RefreshInterval;

    /// <summary>
    /// Countdown until the next refresh. Initialised to a staggered phase at bake time so zones do not
    /// all refresh on the same frame (avoids a synchronised cost spike).
    /// </summary>
    public float RefreshTimer;
}

/// <summary>
/// One effect applied by a <see cref="HazardZone"/>.
/// Burn: <see cref="Magnitude"/> = damage per tick, <see cref="TickRate"/> = seconds between ticks. <br/>
/// Slow: <see cref="Magnitude"/> = speed-reduction multiplier (0.3 = -30%). <br/>
/// <see cref="Linger"/> = how long the effect keeps running after the entity leaves the zone.
/// </summary>
public struct HazardZoneEffectElement : IBufferElementData
{
    public EHazardEffectType Type;
    public float Magnitude;
    public float TickRate;
    public float Linger;
}
