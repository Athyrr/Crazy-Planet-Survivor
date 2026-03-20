using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Component on exp orbs that are currently being attracted to the player.
/// </summary>
public struct AttractionAnimation : IComponentData
{
    public float3 StartPosition;
    public float ElapsedTime;
    public float Duration;
}
