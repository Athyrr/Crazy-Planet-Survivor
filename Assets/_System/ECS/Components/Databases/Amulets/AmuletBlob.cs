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
    public EUpgradeType UpgradeType;
    public ECharacterStat CharacterStat;
    public ESpellStat SpellStat;
    public ESpellTag SpellTags;
    public ESpellID SpellID;
    public EModiferStrategy Strategy;
    public float Value;
}

public struct ApplyAmuletRequest : IComponentData 
{ 
    public int DatabaseIndex; 
}