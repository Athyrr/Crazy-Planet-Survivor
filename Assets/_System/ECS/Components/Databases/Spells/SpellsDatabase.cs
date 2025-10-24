using Unity.Entities;

public struct SpellsDatabase : IComponentData
{
    public BlobAssetReference<SpellBlobs> Blobs;
}
