using _System.Settings;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

/// <summary>
/// Bakes the gameplay numbers of <see cref="CpRaritySettings"/> into the <see cref="RaritySettings"/>
/// singleton consumed by <c>UpgradeSelectionSystem</c>. Place this on the same baked object as
/// <see cref="UpgradesDatabaseAuthoring"/> (e.g. PF_UpgradesDatabase).
/// </summary>
public class RaritySettingsAuthoring : MonoBehaviour
{
    [Tooltip("Rarity settings asset. Auto-resolved in the editor if left empty.")]
    public CpRaritySettings Settings;

    /// <summary>Resolution of the baked luck curve. 33 covers 0..1 in 1/32 steps.</summary>
    private const int LuckSampleCount = 33;

    private void OnValidate()
    {
#if UNITY_EDITOR
        if (Settings == null)
        {
            string[] guids = UnityEditor.AssetDatabase.FindAssets("t:CpRaritySettings");
            if (guids.Length > 0)
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
                Settings = UnityEditor.AssetDatabase.LoadAssetAtPath<CpRaritySettings>(path);
                if (!Application.isPlaying)
                    UnityEditor.EditorUtility.SetDirty(this);
            }
        }
#endif
    }

    private class Baker : Baker<RaritySettingsAuthoring>
    {
        public override void Bake(RaritySettingsAuthoring authoring)
        {
            if (authoring.Settings == null)
            {
                Debug.LogError($"[RaritySettingsAuthoring] '{authoring.name}' needs a CpRaritySettings asset.",
                    authoring);
                return;
            }

            var settings = authoring.Settings;
            DependsOn(settings);

            Entity entity = GetEntity(TransformUsageFlags.None);

            var builder = new BlobBuilder(Allocator.Temp);
            ref RaritySettingsBlob root = ref builder.ConstructRoot<RaritySettingsBlob>();

            root.SpellDropLevelInterval = Mathf.Max(1, settings.SpellDropLevelInterval);
            root.MaxLuck = settings.MaxLuck;

            BlobBuilderArray<float> weights = builder.Allocate(ref root.BaseWeights, RarityConstants.Count);
            for (int i = 0; i < RarityConstants.Count; i++)
                weights[i] = settings.GetBaseWeight((ERarity)i);

            BlobBuilderArray<float> samples = builder.Allocate(ref root.LuckSamples, LuckSampleCount);
            for (int i = 0; i < LuckSampleCount; i++)
            {
                float t = (float)i / (LuckSampleCount - 1);
                samples[i] = settings.SampleLuckFactor(t);
            }

            var blob = builder.CreateBlobAssetReference<RaritySettingsBlob>(Allocator.Persistent);
            AddComponent(entity, new RaritySettings { Blob = blob });
            AddBlobAsset(ref blob, out _);

            builder.Dispose();
        }
    }
}
