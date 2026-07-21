using _System.Settings;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

/// <summary>
/// Bakes the enemy-debuff rules (burn/stun/slow/knockback) and the status-effect VFX prefabs from the
/// shared <see cref="CpCombatEffectsSettings"/> SO into the <see cref="ActiveEffectsConfig"/> and
/// <see cref="ActiveEffectsVfxConfig"/> singletons. The SO is the single source of truth; rebake
/// SC_Entity_Core after changing its values. Life steal lives in <see cref="LifeStealConfigAuthoring"/>.
/// </summary>
public class ActiveEffectsConfigAuthoring : MonoBehaviour
{
    [Tooltip("Shared combat effects settings SO. The debuff rules + VFX prefabs are baked from it.")]
    public CpCombatEffectsSettings Settings;

    private class Baker : Baker<ActiveEffectsConfigAuthoring>
    {
        public override void Bake(ActiveEffectsConfigAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);

            var s = authoring.Settings;
            if (s != null)
                DependsOn(s);

            // Sample the knockback force curve into a blob so the KnockbackSystem can evaluate it under Burst.
            int resolution = s != null ? Mathf.Max(2, s.KnockbackCurveResolution) : 32;
            AnimationCurve kbCurve = s != null && s.KnockbackForceCurve != null
                ? s.KnockbackForceCurve
                : null;

            var builder = new BlobBuilder(Allocator.Temp);
            ref KnockbackCurveBlob root = ref builder.ConstructRoot<KnockbackCurveBlob>();
            BlobBuilderArray<float> samples = builder.Allocate(ref root.Samples, resolution);
            for (int i = 0; i < resolution; i++)
            {
                float t = i / (float)(resolution - 1);
                // Fallback (no SO / no curve): quadratic ease-out (matches the previous t*t behaviour).
                samples[i] = kbCurve != null ? kbCurve.Evaluate(t) : (1f - t) * (1f - t);
            }
            var kbCurveBlob = builder.CreateBlobAssetReference<KnockbackCurveBlob>(Allocator.Persistent);
            builder.Dispose();
            AddBlobAsset(ref kbCurveBlob, out _);

            // Fall back to the design defaults if the SO is unassigned, so the bake never zeroes out.
            AddComponent(entity, new ActiveEffectsConfig
            {
                BurnDamageRatio = s != null ? s.BurnDamageRatio : 0.125f,
                BurnDuration = s != null ? s.BurnDuration : 3f,
                BurnTickRate = s != null ? s.BurnTickRate : 0.3f,

                StunDuration = s != null ? s.StunDuration : 1.5f,

                BaseSlowMultiplier = s != null ? s.SlowMultiplier : 2f,
                SlowDuration = s != null ? s.SlowDuration : 3f,

                KnockbackForce = s != null ? s.KnockbackForce : 50f,
                KnockbackDuration = s != null ? s.KnockbackDuration : 0.5f,
                KnockbackForceCurve = kbCurveBlob,
            });

            AddComponentObject(entity, new ActiveEffectsVfxConfig
            {
                BurnEffectPrefab = s != null ? s.BurnEffectPrefab : null,
                StunEffectPrefab = s != null ? s.StunEffectPrefab : null,
                SlowEffectPrefab = s != null ? s.SlowEffectPrefab : null,
            });
        }
    }
}

public struct ActiveEffectsConfig : IComponentData
{
    // Burn
    public float BurnDamageRatio;
    public float BurnDuration;
    public float BurnTickRate;

    // Stun
    public float StunDuration;

    // Slow
    public float BaseSlowMultiplier;
    public float SlowDuration;

    // Knockback
    public float KnockbackForce;
    public float KnockbackDuration;
    /// <summary>
    /// Baked knockback force curve: samples of Y against normalized elapsed time (0 = impact, 1 = end).
    /// Sampled every frame by <see cref="KnockbackSystem"/> to shape the push falloff.
    /// </summary>
    public BlobAssetReference<KnockbackCurveBlob> KnockbackForceCurve;
}

/// <summary>Uniformly-spaced samples of the knockback force <see cref="UnityEngine.AnimationCurve"/> on 0..1.</summary>
public struct KnockbackCurveBlob
{
    public BlobArray<float> Samples;
}

public class ActiveEffectsVfxConfig : IComponentData
{
    public GameObject BurnEffectPrefab;
    public GameObject StunEffectPrefab;
    public GameObject SlowEffectPrefab;
}