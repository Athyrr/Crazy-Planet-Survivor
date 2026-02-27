using Unity.Entities;

public struct AmuletBlobs
{
    public BlobArray<AmuletBlob> Amulets;
}

public struct AmuletBlob
{
    public BlobString DisplayName;
    public BlobString Description;

    public BlobArray<AmuletModifierBlob> Modifiers;
}

public struct AmuletModifierBlob
{
    public ECharacterStat CharacterStat;
    public EStatModiferStrategy ModifierStrategy;
    public float Value;
}