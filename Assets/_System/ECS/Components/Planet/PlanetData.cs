using Unity.Collections;
using Unity.Entities;

public struct PlanetData : IComponentData
{
    public Entity Prefab;

    [ReadOnly]
    public BlobAssetReference<PlanetHeightMapBlob> HeightDataReference;

    // UV resultion
    public int FaceResolution;

    // Height magnitude
    public float MaxHeight;

    // Water lvl
    public float Radius;
}

public struct PlanetHeightMapBlob
{
    public BlobArray<float> Heights;
}