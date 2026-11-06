using Unity.Entities;
using Unity.Mathematics;

/// <summary>One emission request, appended through ECB.AppendToBuffer onto the EmitRequestQueue singleton by
/// SpellCastingSystem (OnActivation) and CollisionSystem (OnImpact), drained by SpellEmissionSystem the next
/// frame (2-frame latency, decision Q1).</summary>
public struct EmitRequest : IBufferElementData
{
    /// <summary>Root spell (POWER): its ActiveSpell on the caster gives damage/size/crit/capabilities.</summary>
    public int RootDatabaseIndex;
    /// <summary>Spell whose Emitters[] are evaluated (FORM of the triggering instance).</summary>
    public int ShapeDatabaseIndex;
    public Entity Caster;
    public EEmitterTrigger Trigger;
    public float3 Position;
    public float3 Direction;
    public quaternion Rotation;
    /// <summary>OnActivation: the spawned root instance (owner of attached children). Null otherwise.</summary>
    public Entity Owner;
    public Entity LineageId;
    public byte Generation;
    public byte DepthLeft;
    /// <summary>Crit result of the triggering hit (Explode children inherit it, Q7).</summary>
    public bool TriggerCrit;
    /// <summary>Directly hit target, pre-filled (pinned) in the children's HitEntityMemory.</summary>
    public Entity IgnoreTarget;
    /// <summary>Deterministic per-hit seed (Explode Chance roll).</summary>
    public uint RollSeed;
}

/// <summary>Tag of the singleton entity that owns the EmitRequest buffer (created by SpellEmissionSystem).</summary>
public struct EmitRequestQueue : IComponentData { }
