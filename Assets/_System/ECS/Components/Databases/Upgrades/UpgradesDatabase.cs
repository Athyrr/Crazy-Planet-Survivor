using Unity.Entities;

public struct UpgradesDatabase : IComponentData
{
    public BlobAssetReference<UpgradeBlobs> Blobs;
}
