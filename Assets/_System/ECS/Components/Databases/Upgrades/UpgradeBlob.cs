using Unity.Entities;

public struct UpgradeBlob
{
    public BlobString DisplayName;
    public BlobString Description;

    public EUpgradeType UpgradeType;

    public EStatType StatType;
    public EStatModiferStrategy ModifierStrategy;
    public float Value;

    public ESpellID SpellID;
}
