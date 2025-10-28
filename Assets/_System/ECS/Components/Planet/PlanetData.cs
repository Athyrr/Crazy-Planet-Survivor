using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

public struct PlanetData : IComponentData
{
    public Entity Prefab;

    public float3 Center;

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