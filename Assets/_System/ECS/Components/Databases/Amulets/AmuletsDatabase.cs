using Unity.Entities;
using UnityEngine;

public struct AmuletsDatabase : IComponentData
{
    public BlobAssetReference<AmuletBlobs> Blobs;}