using System;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

public class PlanetDataAuthoring : MonoBehaviour
{
    [Tooltip("The heightmap texture for one face of the cube map.")]
    [SerializeField]
    private Texture2D _faceHeightTexture;

    [SerializeField]
    private int _heightMapFaceResolution = 2048;

    [Tooltip("The highest point in the planet from its surface")]
    [SerializeField]
    private float _maxPlanetHeight = 10f;

    [Tooltip("Planet radius (water lvl)")]
    [SerializeField]
    private float _radius = 1f;

    [SerializeField]
    private bool _autoFindPlanetRadius;




    private class Baker : Baker<PlanetDataAuthoring>
    {

        public override void Bake(PlanetDataAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            // Sample height map
            NativeArray<float> tempHeightMapData = ReadHeightMapFromTexture(
            authoring._heightMapFaceResolution,
            authoring._faceHeightTexture
        );

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
                FaceResolution = authoring._heightMapFaceResolution,
                HeightDataReference = heightMapBlobRef
            });

            // Register blob asset
            AddBlobAsset(ref heightMapBlobRef, out var hash);

            // Dispose blob builder and temp arrays
            tempHeightMapData.Dispose();
            blobBuilder.Dispose();
        }

        /// <summary>
        /// Reads a single R x R texture and duplicates it 6 times into the NativeArray.
        /// In a final project, this should project the mesh/texture onto 6 distinct faces.
        /// </summary>
        private NativeArray<float> ReadHeightMapFromTexture(int resolution, Texture2D heightTexture)
        {
            var hm = new NativeArray<float>();
            return hm;
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
