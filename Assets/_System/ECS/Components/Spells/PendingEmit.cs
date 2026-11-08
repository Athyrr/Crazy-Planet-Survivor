using Unity.Entities;

/// <summary>Deferred child of a Sequential emitter (lot 2, D9): one ticket entity per child i ≥ 1, created by
/// SpellEmissionSystem.EmitJob with everything the immediate spawn would have written, frozen at request time
/// (D5: neither the caster nor its ActiveSpell is re-read). ProcessTicketsJob counts Remaining down (frozen while
/// the game is paused), spawns the child through the shared spawn body once Remaining ≤ 0 and the frame cap
/// allows it (otherwise it waits for the next frame: never dropped), then destroys the ticket. All tickets are
/// destroyed when there is no player any more.</summary>
public struct PendingEmit : IComponentData
{
    /// <summary>Resolved spawn of this child: Position / Rotation / Direction already computed for its index.</summary>
    public SpellSpawnParams Params;
    public SpellLineage Lineage;
    public EmittedHitRules Rules;
    /// <summary>Child SO ReHitDelayOverride; 0 = no ReHitDelay component.</summary>
    public float ReHitDelay;
    /// <summary>Target pinned in the child's HitEntityMemory; Null = none.</summary>
    public Entity IgnoreTarget;
    /// <summary>Seconds left before the spawn (i × SequenceInterval at creation).</summary>
    public float Remaining;
}
