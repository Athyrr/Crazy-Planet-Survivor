using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Per-entity, baked dash configuration (geometry + feel). Non-upgradeable design values.
/// The number of charges and their recharge time are read from <see cref="CoreStats"/>
/// (DashCount / DashCooldown) so they can be upgraded, while these stay fixed per prefab.
/// </summary>
public struct DashSettings : IComponentData
{
    /// <summary>Maximum surface distance (world units) covered by a full, unobstructed dash.</summary>
    public float Distance;

    /// <summary>Duration in seconds of the dash motion (the tween from start to end).</summary>
    public float Duration;

    /// <summary>Collider radius used for the free-zone / path-blocking overlap checks.</summary>
    public float Radius;

    /// <summary>Number of samples used to walk the geodesic path when resolving obstacles.</summary>
    public int PathSampleCount;

    /// <summary>Short lockout in seconds after a dash before another dash can start (anti double-dash).</summary>
    public float GlobalCooldown;

    /// <summary>When true the entity is invincible for the whole dash duration (i-frames).</summary>
    public bool IFrames;

    /// <summary>
    /// Motion curve sampled into a blob: X = normalized time (0..1), Y = normalized progress (0..1).
    /// Evaluated each frame to interpolate from the start to the resolved end position.
    /// </summary>
    public BlobAssetReference<DashCurveBlob> Curve;
}

/// <summary>Baked samples of the dash <see cref="UnityEngine.AnimationCurve"/> (uniform on 0..1).</summary>
public struct DashCurveBlob
{
    public BlobArray<float> Samples;
}

/// <summary>Runtime charge/cooldown bookkeeping for a dasher.</summary>
public struct DashState : IComponentData
{
    /// <summary>Charges currently available to spend.</summary>
    public int ChargesAvailable;

    /// <summary>Short lockout after a dash to prevent multiple dashes on the same/adjacent frames.</summary>
    public float GlobalCooldownTimer;

    /// <summary>
    /// One independent countdown per spent (missing) charge. Each entry decrements every frame and,
    /// when it reaches zero, refunds exactly one charge — so charges recharge on their own timers
    /// instead of a single shared one. Invariant: ChargesAvailable + RechargeTimers.Length == max charges.
    /// </summary>
    public FixedList64Bytes<float> RechargeTimers;
}

/// <summary>
/// Enabled while a dash is in flight. Carries the pre-resolved start/end so the motion is a pure
/// kinematic tween (non-physical): the landing point is computed once at trigger time.
/// </summary>
public struct ActiveDash : IComponentData, IEnableableComponent
{
    public float3 StartPos;
    public float3 EndPos;
    public float3 Direction;
    public float Elapsed;
    public float Duration;
}

/// <summary>Trigger flag. Enable it (via input or AI) to request a dash on the next DashSystem tick.</summary>
public struct DashRequest : IComponentData, IEnableableComponent { }

/// <summary>
/// Behavioural add-ons applied while an entity is dashing. Toggled/tuned by upgrades and amulets.
/// Off by default; enabling a flag turns the dash into an offensive/defensive tool.
/// </summary>
public struct DashEffect : IComponentData
{
    /// <summary>When true, enemies the dash passes through are knocked back.</summary>
    public bool Knockback;
    /// <summary>Knockback push strength applied to caught enemies.</summary>
    public float KnockbackForce;
    /// <summary>Overlap radius around the dasher used to catch enemies to knock back.</summary>
    public float KnockbackRadius;

    /// <summary>When true, enemy projectiles the dash passes through are reflected back at enemies.</summary>
    public bool Reflect;
    /// <summary>Overlap radius around the dasher used to catch projectiles to reflect.</summary>
    public float ReflectRadius;
    /// <summary>Damage multiplier applied to reflected projectiles (1 = unchanged).</summary>
    public float ReflectDamageMultiplier;
    /// <summary>Speed multiplier applied to reflected projectiles (1 = unchanged).</summary>
    public float ReflectSpeedMultiplier;

    /// <summary>(a) Damage dealt to each enemy the dash passes through, once per dash (0 = off).</summary>
    public float DashDamage;

    /// <summary>(b) Damage a knocked-back enemy deals to other enemies it is flung into (0 = off).</summary>
    public float KnockbackChainDamage;
    /// <summary>(b) Overlap radius around a knocked-back enemy used to find enemies to chain-damage.</summary>
    public float KnockbackChainRadius;
}

/// <summary>
/// Enemies already damaged during the current dash — per-dash dedup for <see cref="DashEffect.DashDamage"/>,
/// so a target caught for several frames is only hit once. Cleared when a new dash starts.
/// </summary>
public struct DashHitEntity : IBufferElementData
{
    public Entity Value;
}

/// <summary>
/// Stamped (and enabled) on an enemy that was knocked back by a dash carrying chain damage. While the
/// enemy is airborne (still has an active knockback), it periodically damages nearby enemies and itself.
/// Disabled once the knockback ends.
/// </summary>
public struct DashChainDamage : IComponentData, IEnableableComponent
{
    public float Damage;
    public float Radius;
    public float TickTimer;
}

/// <summary>
/// Enabled by the dash while i-frames are active. Read by the collision system to skip damage,
/// so dash invincibility never clobbers the debug <see cref="Invincible"/> tag.
/// </summary>
public struct DashIFrames : IComponentData, IEnableableComponent { }
