using Unity.Collections;
using Unity.Entities;
using UnityEngine;

public class EffectTypeConfigAuthoring : MonoBehaviour
{
    public EffectTypeConfigSO Settings;

    private class Baker : Baker<EffectTypeConfigAuthoring>
    {
        public override void Bake(EffectTypeConfigAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);
            var so = authoring.Settings;
            if (so != null)
                DependsOn(so);

            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<EffectTypeConfigBlob>();

            var entries = so != null ? so.Entries : new EffectTypeConfigSO.Entry[0];
            var entryArray = builder.Allocate(ref root.Entries, entries.Length);
            for (int i = 0; i < entries.Length; i++)
            {
                entryArray[i] = new EffectTypeConfigEntry
                {
                    Type = entries[i].Type,
                    Ratio = entries[i].Ratio,
                    BaseMagnitude = entries[i].BaseMagnitude,
                    BaseDuration = entries[i].BaseDuration,
                    TickRate = entries[i].TickRate,
                    StackMode = entries[i].StackMode,
                    MaxStacks = entries[i].MaxStacks,
                    AllowCrit = entries[i].AllowCrit,
                    AllowLifeSteal = entries[i].AllowLifeSteal,
                };
            }

            int resolution = so != null ? Mathf.Max(2, so.KnockbackCurveResolution) : 32;
            AnimationCurve curve = so != null ? so.KnockbackForceCurve : null;
            var samples = builder.Allocate(ref root.KnockbackForceCurveSamples, resolution);
            for (int i = 0; i < resolution; i++)
            {
                float t = i / (float)(resolution - 1);
                samples[i] = curve != null ? curve.Evaluate(t) : (1f - t) * (1f - t);
            }

            var blobRef = builder.CreateBlobAssetReference<EffectTypeConfigBlob>(Allocator.Persistent);
            builder.Dispose();
            AddBlobAsset(ref blobRef, out _);

            AddComponent(entity, new EffectTypeConfig { Blob = blobRef });
        }
    }
}
