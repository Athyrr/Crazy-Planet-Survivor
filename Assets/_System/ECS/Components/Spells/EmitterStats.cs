using Unity.Entities;

/// <summary>Emission counters (on the EmitRequestQueue singleton), printed by the `emitter_stats` console command.</summary>
public struct EmitterStats : IComponentData
{
    public int EmittedLastFrame;
    public int PeakEmittedPerFrame;
    public int DroppedFrameCap;
    public int DroppedLineageBudget;
    public int DroppedInvalid;
}
