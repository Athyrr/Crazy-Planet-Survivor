using Unity.Entities;

/// <summary>
/// Component data for spell that pierce through multiple targets.
/// </summary>
public struct Pierce : IComponentData
{
    public int RemainingPierces;
}
