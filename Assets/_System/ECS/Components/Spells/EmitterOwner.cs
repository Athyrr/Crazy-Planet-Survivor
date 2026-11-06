using Unity.Entities;

/// <summary>Owner of attached children (PF_Spell_EmitterOwner). Holds the common orbit angle; children are
/// placed analytically by EmitterAnchorSystem (no Parent hierarchy).</summary>
public struct EmitterOwner : IComponentData
{
    public float Angle;          // radians
    public float AngularSpeed;   // radians / s
    public int Count;
    public float Radius;
    public EEmitterFrame Frame;
    public int EmitterIndex;     // slot in the root SpellBlob.Emitters
    public float ChildBaseScale; // child prefab LocalTransform.Scale
    public float ChildScale;     // ChildBaseScale × root FinalSize (refreshed on reconcile)
}
