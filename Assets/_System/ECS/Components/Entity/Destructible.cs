using Unity.Entities;

/// <summary>
/// Defines an entity as destructible. Replace CpEntity
/// </summary>
public struct Destructible : IComponentData, IEnableableComponent
{
    public uint LayerMask;
}