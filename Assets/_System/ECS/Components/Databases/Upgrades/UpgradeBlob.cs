using Unity.Entities;

public struct UpgradeBlob
{
    public BlobString DisplayName;
    public BlobString Description;

    public EUpgradeType UpgradeType;

    public ECharacterStat CharacterStat;

    public ESpellID SpellID;
    public ESpellTag SpellTags;
    public ESpellStat SpellStat;

    public EStatModiferStrategy ModifierStrategy;
    public float Value;
}
