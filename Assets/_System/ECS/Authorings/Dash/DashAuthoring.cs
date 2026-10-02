using Unity.Entities;
using UnityEngine;

/// <summary>Add this to any entity that can dash. Bakes the dash geometry/feel from Settings into
/// DashSettings (including the motion curve sampled into a blob) plus the runtime state and the
/// enableable trigger/flags. The charge count and recharge time come from CoreStats (DashCount /
/// DashCooldown), so this component only defines the fixed feel.</summary>
public class DashAuthoring : MonoBehaviour
{
    public DashSO Settings;

    private class Baker : Baker<DashAuthoring>
    {
        public override void Bake(DashAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);
            var so = authoring.Settings;

            if (so != null)
                DependsOn(so);

            int resolution = so != null ? Mathf.Max(2, so.CurveResolution) : 32;
            var builder = new BlobBuilder(Unity.Collections.Allocator.Temp);
            ref DashCurveBlob root = ref builder.ConstructRoot<DashCurveBlob>();
            BlobBuilderArray<float> samples = builder.Allocate(ref root.Samples, resolution);

            AnimationCurve curve = so != null && so.MotionCurve != null
                ? so.MotionCurve
                : AnimationCurve.Linear(0f, 0f, 1f, 1f);
            for (int i = 0; i < resolution; i++)
            {
                float t = i / (float)(resolution - 1);
                samples[i] = curve.Evaluate(t);
            }

            BlobAssetReference<DashCurveBlob> curveBlob =
                builder.CreateBlobAssetReference<DashCurveBlob>(Unity.Collections.Allocator.Persistent);
            builder.Dispose();
            AddBlobAsset(ref curveBlob, out _);

            AddComponent(entity, new DashSettings
            {
                Distance = so != null ? so.ResolvedDistance : 0f,
                Duration = Mathf.Max(0.01f, so != null ? so.ResolvedDuration : 0.01f),
                Radius = Mathf.Max(0.01f, so != null ? so.ResolvedRadius : 0.01f),
                PathSampleCount = Mathf.Max(2, so != null ? so.ResolvedPathSampleCount : 2),
                GlobalCooldown = Mathf.Max(0f, so != null ? so.ResolvedGlobalCooldown : 0f),
                IFrames = so != null && so.ResolvedIFrames,
                Curve = curveBlob,
            });

            AddComponent(entity, new DashState { ChargesAvailable = -1, GlobalCooldownTimer = 0f });

            AddComponent<ActiveDash>(entity);
            SetComponentEnabled<ActiveDash>(entity, false);

            AddComponent<DashRequest>(entity);
            SetComponentEnabled<DashRequest>(entity, false);

            AddComponent<DashIFrames>(entity);
            SetComponentEnabled<DashIFrames>(entity, false);

            AddComponent(entity, new DashEffect
            {
                Knockback = so != null && so.ResolvedKnockback,
                KnockbackForce = so != null ? so.ResolvedKnockbackForce : 0f,
                KnockbackRadius = so != null ? so.ResolvedKnockbackRadius : 0f,
                Reflect = so != null && so.ResolvedReflect,
                ReflectRadius = so != null ? so.ResolvedReflectRadius : 0f,
                ReflectDamageMultiplier = so != null ? so.ResolvedReflectDamageMultiplier : 1f,
                ReflectSpeedMultiplier = so != null ? so.ResolvedReflectSpeedMultiplier : 1f,
                DashDamage = so != null ? so.ResolvedDashDamage : 0f,
                KnockbackChainDamage = so != null ? so.ResolvedKnockbackChainDamage : 0f,
                KnockbackChainRadius = so != null ? so.ResolvedKnockbackChainRadius : 0f,
            });

            AddBuffer<DashHitEntity>(entity);
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (Settings == null) return;

        if (Settings.ResolvedKnockback && Settings.ResolvedKnockbackRadius > 0f)
        {
            Gizmos.color = new Color(1f, 0.35f, 0.15f, 0.9f);
            Gizmos.DrawWireSphere(transform.position, Settings.ResolvedKnockbackRadius);
        }

        if (Settings.ResolvedReflect && Settings.ResolvedReflectRadius > 0f)
        {
            Gizmos.color = new Color(0.35f, 0.75f, 1f, 0.8f);
            Gizmos.DrawWireSphere(transform.position, Settings.ResolvedReflectRadius);
        }
    }
#endif
}
