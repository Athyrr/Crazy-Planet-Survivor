using Unity.Collections;
using Unity.Entities;
using UnityEngine;

public class LootDatabaseAuthoring : MonoBehaviour
{
    [Header("Resources Database")]
    [SerializeField] private ResourceDatabaseSO _resourceDatabase;

    [Header("Experience Orbs")] public ExperienceOrbAuthoring[] ExpOrbPrefabs;

    [Header("Attraction Animation")]
    [Tooltip("Attraction animation duration.")]
    public float AttractionDuration = 1.5f;

    [Tooltip("Orb animation progression (0 = start pos, 1 = end pos (player).")]
    public AnimationCurve AttractionCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Tooltip("Curve precision.")] public int Resolution = 64;

    private class Baker : Baker<LootDatabaseAuthoring>
    {
        public override void Bake(LootDatabaseAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);

            // Experience Orbs Database Buffer
            var expOrbBuffer = AddBuffer<ExpOrbDatabaseBufferElement>(entity);
            var orderedExpOrbs = new NativeList<ExpOrbDatabaseBufferElement>(Allocator.Temp);
            foreach (var orbPrefab in authoring.ExpOrbPrefabs)
            {
                if (orbPrefab == null)
                    continue;

                orderedExpOrbs.Add(new ExpOrbDatabaseBufferElement
                {
                    Prefab = GetEntity(orbPrefab.gameObject, TransformUsageFlags.Dynamic),
                    Value = orbPrefab.Value
                });
            }

            orderedExpOrbs.Sort();

            expOrbBuffer.AddRange(orderedExpOrbs.AsArray());
            orderedExpOrbs.Dispose();

            // Resources Database Buffer — built from ResourceDatabaseSO
            var resourceBuffer = AddBuffer<ResourcesDatabaseBufferElement>(entity);
            if (authoring._resourceDatabase != null)
            {
                foreach (var resource in authoring._resourceDatabase.Resources)
                {
                    if (resource == null || resource.OrbPrefab == null)
                        continue;

                    resourceBuffer.Add(new ResourcesDatabaseBufferElement
                    {
                        Prefab = GetEntity(resource.OrbPrefab, TransformUsageFlags.Dynamic),
                        Type = resource.Type
                    });
                }
            }

            // Orb animation curve
            using var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<AttractionAnimationCurveBlob>();

            BlobBuilderArray<float> arrayBuilder = builder.Allocate(ref root.Samples, authoring.Resolution);

            for (int i = 0; i < authoring.Resolution; i++)
            {
                float t = (float)i / (authoring.Resolution - 1);
                arrayBuilder[i] = authoring.AttractionCurve.Evaluate(t);
            }

            root.Duration = authoring.AttractionDuration;
            root.SampleCount = authoring.Resolution;

            var animCurveBlobRef = builder.CreateBlobAssetReference<AttractionAnimationCurveBlob>(Allocator.Persistent);

            AddComponent(entity, new AttractionAnimationCurveConfig
            {
                CurveBlobRef = animCurveBlobRef
            });

            AddBlobAsset(ref animCurveBlobRef, out var hash);
        }
    }
}
