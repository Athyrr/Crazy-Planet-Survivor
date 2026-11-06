using Unity.Entities;

/// <summary>Attached child: position = caster + Radius × dir(owner.Angle + 2π·Index/Count).</summary>
public struct EmitterAnchor : IComponentData
{
    public Entity Owner;
    public int Index;
}
