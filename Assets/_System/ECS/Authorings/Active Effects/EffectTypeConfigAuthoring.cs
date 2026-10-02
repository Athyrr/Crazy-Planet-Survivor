using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using _System.Settings;

/// <summary>Bakes both the status-effect rules (EffectTypeConfig, LifeStealConfig — from EffectSettings)
/// and the status-effect VFX prefabs (ActiveEffectsVfxConfig — from VfxSettings). Merged from what used to
/// be 3 separate Authorings (ActiveEffectsConfigAuthoring, EffectTypeConfigAuthoring,
/// LifeStealConfigAuthoring) — all 3 only ever baked from the same prefab, so the split isolated nothing.</summary>
public class EffectTypeConfigAuthoring : MonoBehaviour
{
    public CpEffectTypeSettings EffectSettings;
    public CpCombatEffectsSettings VfxSettings;

    private class Baker : Baker<EffectTypeConfigAuthoring>
    {
        public override void Bake(EffectTypeConfigAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);
            var effectSettings = authoring.EffectSettings;
            var vfxSettings = authoring.VfxSettings;

            if (effectSettings != null)
                DependsOn(effectSettings);
            if (vfxSettings != null)
                DependsOn(vfxSettings);

            // --- EffectTypeConfig (Burn/Slow/Stun/Knockback rules blob) ---
            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<EffectTypeConfigBlob>();

            var entries = effectSettings != null ? effectSettings.Entries : new CpEffectTypeSettings.Entry[0];
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

            int resolution = effectSettings != null ? Mathf.Max(2, effectSettings.KnockbackCurveResolution) : 32;
            AnimationCurve curve = effectSettings != null ? effectSettings.KnockbackForceCurve : null;
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

            // --- LifeStealConfig ---
            AddComponent(entity, new LifeStealConfig
            {
                Conversion = effectSettings != null ? effectSettings.LifeStealConversion : 0.075f,
                ProcCooldown = effectSettings != null ? effectSettings.LifeStealProcCooldown : 0.1f,
            });

            // --- ActiveEffectsVfxConfig ---
            AddComponentObject(entity, new ActiveEffectsVfxConfig
            {
                BurnEffectPrefab = vfxSettings != null ? vfxSettings.BurnEffectPrefab : null,
                StunEffectPrefab = vfxSettings != null ? vfxSettings.StunEffectPrefab : null,
                SlowEffectPrefab = vfxSettings != null ? vfxSettings.SlowEffectPrefab : null,
            });
        }
    }
}

public class ActiveEffectsVfxConfig : IComponentData
{
    public GameObject BurnEffectPrefab;
    public GameObject StunEffectPrefab;
    public GameObject SlowEffectPrefab;
}

/// <summary>
/// Global life-steal rules (constants, not a per-entity stat). The proc CHANCE is a stat on
/// <see cref="CoreStats"/>; this only holds the conversion ratio and the rate limiter.
/// </summary>
public struct LifeStealConfig : IComponentData
{
    /// <summary>Fraction of a hit's damage healed on a successful proc (0.075 = 7.5%).</summary>
    public float Conversion;

    /// <summary>Minimum seconds between two procs (0.1 = max 10 procs/s).</summary>
    public float ProcCooldown;
}
