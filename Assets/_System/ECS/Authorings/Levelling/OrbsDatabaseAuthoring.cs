using Unity.Collections;
using Unity.Entities;
using UnityEngine;

public class OrbsDatabaseAuthoring : MonoBehaviour
{
    [Header("Orbs Database")]

    public ExperienceOrbAuthoring[] OrbPrefabs;


    [Header("Attraction Animation")]

    [Tooltip("Attraction animation duration.")]
    public float AttractionDuration = 1.5f;

    [Tooltip("Orb animation progression (0 = start pos, 1 = end pos (player).")]
    public AnimationCurve AttractionCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Tooltip("Curve precision.")]
    public int Resolution = 64;

    private class Baker : Baker<OrbsDatabaseAuthoring>
    {
        public override void Bake(OrbsDatabaseAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);

            // Orbs Database Buffer
            var buffer = AddBuffer<OrbDatabaseBufferElement>(entity);

            var orderedOrbs = new NativeList<OrbDatabaseBufferElement>(Allocator.Temp);

            foreach (var orbPrefab in authoring.OrbPrefabs)
            {
                if (orbPrefab == null)
                    continue;

                orderedOrbs.Add(new OrbDatabaseBufferElement
                {
                    Prefab = GetEntity(orbPrefab.gameObject, TransformUsageFlags.Dynamic),
                    Value = orbPrefab.Value
                });
            }

            // Sort by descending
            orderedOrbs.Sort();
            // Set buffer
            buffer.AddRange(orderedOrbs.AsArray());
            //Clean up
            orderedOrbs.Dispose();



            // Orb animation curve
            using var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<ExpAttractionAnimationCurveBlob>();

            var arrayBuilder = builder.Allocate(ref root.Samples, authoring.Resolution);

            for (int i = 0; i < authoring.Resolution; i++)
            {
                float t = (float)i / (authoring.Resolution - 1);
                arrayBuilder[i] = authoring.AttractionCurve.Evaluate(t);
            }

            root.Duration = authoring.AttractionDuration;
            root.SampleCount = authoring.Resolution;

            var blobRef = builder.CreateBlobAssetReference<ExpAttractionAnimationCurveBlob>(Allocator.Persistent);

            AddComponent(entity, new ExpAttractionAnimationCurveConfig
            {
                CurveBlobRef = blobRef
            });
        }
    }
}