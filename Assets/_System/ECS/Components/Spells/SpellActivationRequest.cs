using Unity.Entities;

public struct SpellActivationRequest : IBufferElementData
{
    public ESpellID ID;
}
