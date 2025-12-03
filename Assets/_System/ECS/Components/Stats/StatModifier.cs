using Unity.Entities;

[System.Serializable]
public struct StatModifier : IBufferElementData
{
    public EStatID StatID;
    public float Value;
    public EStatModiferStrategy Strategy;

}

