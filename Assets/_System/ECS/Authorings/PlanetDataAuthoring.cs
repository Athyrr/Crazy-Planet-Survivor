using Unity.Collections;
using Unity.Mathematics;
using Unity.Entities;
using UnityEngine;

public class PlanetDataAuthoring : MonoBehaviour
{
    [Tooltip("The heightmap atlas texture.")]
    [SerializeField]
    private Texture2D _heightMapTexture;

    [SerializeField]
    private int _atlasPixelWidth = 2048;

    [Tooltip("The maximum height of the terrain.")]
    [SerializeField]
    private float _maxPlanetHeight = 10f;

    [Tooltip("Base planet radius (sea level)")]
    [SerializeField]
    private float _radius = 50f;

    [SerializeField]
    private bool _autoFindPlanetRadius;

    [SerializeField]
    private bool _useHeightMap = true;

    private class Baker : Baker<PlanetDataAuthoring>
    {
        public override void Bake(PlanetDataAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            // Face resolution 
            int R = authoring._atlasPixelWidth / 4; // Altas width has 4 faces in width 

            // Atlas resolution ( cube faces + dead zones )
            int totalAtlasResolution = authoring._atlasPixelWidth;

            // Sample height map
            NativeArray<float> tempHeightMapData = authoring._useHeightMap ?
                ReadHeightMapFromAtlas(R, authoring._heightMapTexture, totalAtlasResolution) : new NativeArray<float>(R * R * 6, Allocator.TempJob);

            // Create blob asset
            var blobBuilder = new BlobBuilder(Allocator.Temp);
            ref PlanetHeightMapBlob root = ref blobBuilder.ConstructRoot<PlanetHeightMapBlob>();

            BlobBuilderArray<float> heightsArray = blobBuilder.Allocate(ref root.Heights, tempHeightMapData.Length);

            // Fullfill blob array
            for (int i = 0; i < tempHeightMapData.Length; i++)
            {
                heightsArray[i] = tempHeightMapData[i];
            }

            BlobAssetReference<PlanetHeightMapBlob> heightMapBlobRef = blobBuilder.CreateBlobAssetReference<PlanetHeightMapBlob>(Allocator.Persistent);

            // Set component
            AddComponent(entity, new PlanetData()
            {
                Radius = authoring._radius,
                MaxHeight = authoring._maxPlanetHeight,
                FaceResolution = R,
                HeightDataReference = heightMapBlobRef
            });

            // Register blob asset
            AddBlobAsset(ref heightMapBlobRef, out var hash);

            // Dispose blob builder and temp arrays
            tempHeightMapData.Dispose();
            blobBuilder.Dispose();
        }

        /// <summary>
        /// Reads heightmap atlas and generate a NativeArray 1D with its values.
        /// </summary>
        /// <param name="R">Face resolution (just one axis).</param>
        /// <param name="atlas">Atlas texture.</param>
        /// <param name="AtlasResolution">Atlas resolution.</param>
        private NativeArray<float> ReadHeightMapFromAtlas(int R, Texture2D atlas, int AtlasResolution)
        {
            int totalSize = R * R * 6;

            if (atlas == null)
            {
                Debug.LogError("Heightmap texture not found in planet Prefab.");
                return new NativeArray<float>(totalSize, Allocator.TempJob);
            }
            if (!atlas.isReadable)
            {
                Debug.LogError("Cannot read texture. Enable Read/Write texture.");
                return new NativeArray<float>(totalSize, Allocator.TempJob);
            }
            if (atlas.width != AtlasResolution || atlas.height != AtlasResolution)
            {
                Debug.LogWarning($"Atlas resolution ({atlas.width}x{atlas.height}) " +
                                 $"does not correspond to _heightMapResolution ({AtlasResolution})."
                                 );
            }

            // Init height map data
            NativeArray<float> heightMapData = new NativeArray<float>(totalSize, Allocator.TempJob);

            Color[] atlasData = atlas.GetPixels();
            int atlasWidth = atlas.width;

            int2[] faceOffsets = new int2[]
            {
                new int2(1 * R, 2 * R), // Index 0: X+ (Grid 1, 2)
                new int2(1 * R, 0 * R), // Index 1: X- (Grid 1, 0)
                new int2(2 * R, 2 * R), // Index 2: Y+ (Grid 2, 2)
                new int2(0 * R, 2 * R), // Index 3: Y- (Grid 0, 2)
                new int2(1 * R, 1 * R), // Index 4: Z+ (Grid 1, 1)
                new int2(1 * R, 3 * R)  // Index 5: Z- (Grid 1, 3)
            };

            int dataIndex = 0;

            for (int face = 0; face < 6; face++)
            {
                int faceOffsetX = faceOffsets[face].x;
                int faceOffsetY = faceOffsets[face].y;

                // Vertical (V) bottom to top
                for (int y = 0; y < R; y++)
                {
                    // Horizontal (U) left to right
                    for (int x = 0; x < R; x++)
                    {
                        int pixelX = faceOffsetX + x;
                        int pixelY = faceOffsetY + y;

                        int atlasIndex = pixelY * atlasWidth + pixelX;
                        Color pixelColor = atlasData[atlasIndex];

                        float heightValue = pixelColor.r;
                        heightMapData[dataIndex] = heightValue;
                        dataIndex++;
                    }
                }
            }

            return heightMapData;
        }
    }

    private void OnValidate()
    {
        if (_autoFindPlanetRadius)
        {
            var mesh = GetComponent<MeshFilter>();
            if (mesh && mesh.sharedMesh)
                _radius = 0.5f * mesh.sharedMesh.bounds.size.x * transform.lossyScale.x;
        }
    }
}
