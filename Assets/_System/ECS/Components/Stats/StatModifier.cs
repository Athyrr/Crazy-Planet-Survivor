using Unity.Entities;

[System.Serializable]
public struct StatModifier : IBufferElementData
{
    public EStatType Type;
    public float Value;
    public EStatModiferStrategy Strategy;

}

