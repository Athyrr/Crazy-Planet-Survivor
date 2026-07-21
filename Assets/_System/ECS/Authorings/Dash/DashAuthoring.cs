using Unity.Entities;
using UnityEngine;

/// <summary>
/// Add this to any entity that can dash (the player, or enemies meant to lunge). It bakes the dash
/// geometry/feel into <see cref="DashSettings"/> (including the motion curve sampled into a blob) plus
/// the runtime state and the enableable trigger/flags. The charge count and recharge time come from
/// <see cref="CoreStats"/> (DashCount / DashCooldown), so this component only defines the fixed feel.
/// </summary>
public class DashAuthoring : MonoBehaviour
{
    [Header("Geometry")]
    [Tooltip("Max surface distance (world units) of a full, unobstructed dash.")]
    public float Distance = 6f;

    [Tooltip("Duration in seconds of the dash motion.")]
    public float Duration = 0.15f;

    [Tooltip("Collider radius used for the free-zone / path-blocking checks.")]
    public float Radius = 0.5f;

    [Tooltip("Number of samples used to walk the path when resolving obstacles (higher = more precise).")]
    [Range(2, 32)] public int PathSampleCount = 8;

    [Header("Feel")]
    [Tooltip("Lockout in seconds after a dash before another can start (prevents same-frame double dash).")]
    public float GlobalCooldown = 0.2f;

    [Tooltip("If true, the entity is invincible for the whole dash duration.")]
    public bool IFrames = true;

    [Tooltip("Motion curve: X = normalized time (0..1), Y = normalized progress (0..1). " +
             "Linear = constant speed; ease-out = fast start then settle.")]
    public AnimationCurve MotionCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Tooltip("How many points the curve is sampled into for Burst evaluation.")]
    [Range(8, 128)] public int CurveResolution = 32;

    [Header("Dash Effects (usually enabled by upgrades)")]
    [Tooltip("Knock enemies back when the dash passes through them.")]
    public bool Knockback = false;
    [Tooltip("Push strength applied to caught enemies.")]
    public float KnockbackForce = 30f;
    [Tooltip("Radius around the dasher used to catch enemies to knock back.")]
    public float KnockbackRadius = 2f;

    [Tooltip("Reflect enemy projectiles the dash passes through back at their casters.")]
    public bool Reflect = false;
    [Tooltip("Radius around the dasher used to catch projectiles to reflect.")]
    public float ReflectRadius = 2.5f;
    [Tooltip("Damage multiplier applied to reflected projectiles (1 = unchanged).")]
    public float ReflectDamageMultiplier = 1f;
    [Tooltip("Speed multiplier applied to reflected projectiles (1 = unchanged).")]
    public float ReflectSpeedMultiplier = 1f;

    [Tooltip("(a) Damage dealt to each enemy the dash passes through, once per dash (0 = off).")]
    public float DashDamage = 0f;

    [Tooltip("(b) Damage a knocked-back enemy deals to enemies it is flung into (0 = off).")]
    public float KnockbackChainDamage = 0f;
    [Tooltip("(b) Radius around a knocked-back enemy used to find enemies to chain-damage.")]
    public float KnockbackChainRadius = 1.5f;

    private class Baker : Baker<DashAuthoring>
    {
        public override void Bake(DashAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            // Sample the AnimationCurve into a blob so the Burst dash job can evaluate it.
            int resolution = Mathf.Max(2, authoring.CurveResolution);
            var builder = new BlobBuilder(Unity.Collections.Allocator.Temp);
            ref DashCurveBlob root = ref builder.ConstructRoot<DashCurveBlob>();
            BlobBuilderArray<float> samples = builder.Allocate(ref root.Samples, resolution);

            AnimationCurve curve = authoring.MotionCurve ?? AnimationCurve.Linear(0f, 0f, 1f, 1f);
            for (int i = 0; i < resolution; i++)
            {
                float t = i / (float)(resolution - 1);
                samples[i] = curve.Evaluate(t);
            }

            BlobAssetReference<DashCurveBlob> curveBlob =
                builder.CreateBlobAssetReference<DashCurveBlob>(Unity.Collections.Allocator.Persistent);
            builder.Dispose();

            // Register for lifetime management + de-duplication across identical curves.
            AddBlobAsset(ref curveBlob, out _);

            AddComponent(entity, new DashSettings
            {
                Distance = authoring.Distance,
                Duration = Mathf.Max(0.01f, authoring.Duration),
                Radius = Mathf.Max(0.01f, authoring.Radius),
                PathSampleCount = Mathf.Max(2, authoring.PathSampleCount),
                GlobalCooldown = Mathf.Max(0f, authoring.GlobalCooldown),
                IFrames = authoring.IFrames,
                Curve = curveBlob,
            });

            // -1 = uninitialized: the DashSystem tops up to CoreStats.DashCount (full) on the first tick,
            // so the entity starts with all its charges without waiting a recharge cycle.
            AddComponent(entity, new DashState
            {
                ChargesAvailable = -1,
                GlobalCooldownTimer = 0f,
            });

            AddComponent<ActiveDash>(entity);
            SetComponentEnabled<ActiveDash>(entity, false);

            AddComponent<DashRequest>(entity);
            SetComponentEnabled<DashRequest>(entity, false);

            AddComponent<DashIFrames>(entity);
            SetComponentEnabled<DashIFrames>(entity, false);

            AddComponent(entity, new DashEffect
            {
                Knockback = authoring.Knockback,
                KnockbackForce = authoring.KnockbackForce,
                KnockbackRadius = authoring.KnockbackRadius,
                Reflect = authoring.Reflect,
                ReflectRadius = authoring.ReflectRadius,
                ReflectDamageMultiplier = authoring.ReflectDamageMultiplier,
                ReflectSpeedMultiplier = authoring.ReflectSpeedMultiplier,
                DashDamage = authoring.DashDamage,
                KnockbackChainDamage = authoring.KnockbackChainDamage,
                KnockbackChainRadius = authoring.KnockbackChainRadius,
            });

            // Per-dash dedup list for DashDamage (which enemies were already hit this dash).
            AddBuffer<DashHitEntity>(entity);
        }
    }

#if UNITY_EDITOR
    // Draws the dash-effect radii around the dasher so the designer can tune when contact triggers.
    // Only shown while this component is selected (i.e. picked in the hierarchy or project view).
    private void OnDrawGizmosSelected()
    {
        if (Knockback && KnockbackRadius > 0f)
        {
            Gizmos.color = new Color(1f, 0.35f, 0.15f, 0.9f); // warm orange = knockback / dash damage contact
            Gizmos.DrawWireSphere(transform.position, KnockbackRadius);
        }

        if (Reflect && ReflectRadius > 0f)
        {
            Gizmos.color = new Color(0.35f, 0.75f, 1f, 0.8f); // cool blue = reflect
            Gizmos.DrawWireSphere(transform.position, ReflectRadius);
        }
    }
#endif
}
