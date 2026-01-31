using Unity.Entities;

/// <summary>
/// Component data for spell that pierce through multiple targets.
/// </summary>
public struct Pierce : IComponentData, IEnableableComponent
{
    public int RemainingPierces;
}
