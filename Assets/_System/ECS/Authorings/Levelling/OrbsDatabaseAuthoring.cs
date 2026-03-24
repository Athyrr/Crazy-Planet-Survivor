using _System.ECS.Authorings.Ressources;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

public class OrbsDatabaseAuthoring : MonoBehaviour
{
    [Header("Ressources part")]
    [SerializeField] private EnumValues<ERessourceType, RessourceAuthoring> _ressourceTypes;
    
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
            var orbBuffer = AddBuffer<OrbDatabaseBufferElement>(entity);
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

            orderedOrbs.Sort();
            
            orbBuffer.AddRange(orderedOrbs.AsArray());
            orderedOrbs.Dispose();

            // Ressources Database Buffer
            
            var ressourcesbuffer = AddBuffer<RessourcesDatabaseBufferElement>(entity);
            var orderedRessources = new NativeList<RessourcesDatabaseBufferElement>(Allocator.Temp);
            foreach ((var lootType, var ressourceAuthoring) in authoring._ressourceTypes)
            {
                if (ressourceAuthoring == null)
                    continue;

                orderedRessources.Add(new RessourcesDatabaseBufferElement
                {
                    Prefab = GetEntity(ressourceAuthoring.gameObject, TransformUsageFlags.Dynamic),
                    Value = 1
                });
            }

            orderedRessources.Sort();
            
            ressourcesbuffer.AddRange(orderedRessources.AsArray());
            orderedRessources.Dispose();

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

            //builder.Dispose();
        }
    }
}