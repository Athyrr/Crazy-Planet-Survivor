using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

/// <summary>Channeled-beam ray (attached emitter child, spec channeled-beam). TurnRateRadians and OriginHeight are the
/// FORM baked by BeamAuthoring; the rest is runtime state owned by BeamAimSystem.</summary>
public struct BeamAim : IComponentData
{
    public float TurnRateRadians;   // max turn rate toward the target (rad/s), from BeamAuthoring (°/s)
    public float OriginHeight;      // visual only: both chord ends raised along their normal (m)
    public Entity Target;           // Entity.Null = no target → BeamActive disabled
    public bool Doubling;           // shares its target with another ray (fewer targets than rays)
    public float3 Direction;        // current aim, unit tangent at the caster (re-projected every frame)
    public float RetargetTimer;     // seconds until the next validity / distribution pass
    public bool Served;             // false until the first pass: a new ray is served on its first frame
}

/// <summary>Enableable: the ray is lit (it has a target). Disabled → AreaAttackSystem's OverTime job neither queries
/// nor ticks the zone and clears its tracked targets. Baked disabled by BeamAuthoring.</summary>
public struct BeamActive : IComponentData, IEnableableComponent { }

/// <summary>Visual prefab of the ray (BeamVisual at its root). Resolved on the main thread only, by
/// BeamVisualPresentationSystem; the prefab is never converted to an entity.</summary>
public struct BeamVisualPrefab : IComponentData
{
    public UnityObjectRef<GameObject> Value;
}
