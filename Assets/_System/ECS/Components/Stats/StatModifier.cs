using Unity.Entities;

[System.Serializable]
public struct StatModifier : IBufferElementData
{
    public ECharacterStat StatID;
    public float Value;
    public EStatModiferStrategy Strategy;
}

