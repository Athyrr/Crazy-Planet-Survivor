using Unity.Entities;

/// <summary>Runtime of an allied agent posed by a Summon spell (spec allegeance-summon §2). Its group is
/// (Owner, SourceDatabaseIndex): siblings are found through that pair (future turret link, _notes/lien-electrique-tourelles).</summary>
public struct Summon : IComponentData
{
    /// <summary>The caster who equipped the Summon spell (the player in this version).</summary>
    public Entity Owner;

    /// <summary>DB index of the owner's Summon spell — stable, unlike the index in the owner's ActiveSpell buffer.</summary>
    public int SourceDatabaseIndex;

    /// <summary>0 .. FinalAmount-1 inside its group.</summary>
    public int Slot;

    /// <summary>Elapsed time of the next relocation (EndOfLife Relocate); double.MaxValue = permanent. An absolute time,
    /// not a countdown, so nothing writes this component every frame (no sync point in SummonReconcileSystem).</summary>
    public double ExpiresAt;
}
