using UnityEngine;

[CreateAssetMenu(fileName = "NewDash", menuName = "Survivor/Characters/Dash")]
public class DashSO : CharacterSettingsSO
{
    [Tooltip("Null on the base asset itself. Every per-character asset points at the shared default.")]
    public DashSO BaseTemplate;

    [Header("Geometry")]
    public Overridable<float> Distance;
    public Overridable<float> Duration;
    public Overridable<float> Radius;
    public Overridable<int> PathSampleCount;

    [Header("Feel")]
    public Overridable<float> GlobalCooldown;
    public Overridable<bool> IFrames;
    public AnimationCurve MotionCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [Range(8, 128)] public int CurveResolution = 32;

    [Header("Dash Effects")]
    public Overridable<bool> Knockback;
    public Overridable<float> KnockbackForce;
    public Overridable<float> KnockbackRadius;
    public Overridable<bool> Reflect;
    public Overridable<float> ReflectRadius;
    public Overridable<float> ReflectDamageMultiplier;
    public Overridable<float> ReflectSpeedMultiplier;
    public Overridable<float> DashDamage;
    public Overridable<float> KnockbackChainDamage;
    public Overridable<float> KnockbackChainRadius;

    public float ResolvedDistance => Distance.Resolve(BaseTemplate != null ? BaseTemplate.ResolvedDistance : 0f);
    public float ResolvedDuration => Duration.Resolve(BaseTemplate != null ? BaseTemplate.ResolvedDuration : 0f);
    public float ResolvedRadius => Radius.Resolve(BaseTemplate != null ? BaseTemplate.ResolvedRadius : 0f);
    public int ResolvedPathSampleCount => PathSampleCount.Resolve(BaseTemplate != null ? BaseTemplate.ResolvedPathSampleCount : 0);
    public float ResolvedGlobalCooldown => GlobalCooldown.Resolve(BaseTemplate != null ? BaseTemplate.ResolvedGlobalCooldown : 0f);
    public bool ResolvedIFrames => IFrames.Resolve(BaseTemplate != null && BaseTemplate.ResolvedIFrames);
    public bool ResolvedKnockback => Knockback.Resolve(BaseTemplate != null && BaseTemplate.ResolvedKnockback);
    public float ResolvedKnockbackForce => KnockbackForce.Resolve(BaseTemplate != null ? BaseTemplate.ResolvedKnockbackForce : 0f);
    public float ResolvedKnockbackRadius => KnockbackRadius.Resolve(BaseTemplate != null ? BaseTemplate.ResolvedKnockbackRadius : 0f);
    public bool ResolvedReflect => Reflect.Resolve(BaseTemplate != null && BaseTemplate.ResolvedReflect);
    public float ResolvedReflectRadius => ReflectRadius.Resolve(BaseTemplate != null ? BaseTemplate.ResolvedReflectRadius : 0f);
    public float ResolvedReflectDamageMultiplier => ReflectDamageMultiplier.Resolve(BaseTemplate != null ? BaseTemplate.ResolvedReflectDamageMultiplier : 1f);
    public float ResolvedReflectSpeedMultiplier => ReflectSpeedMultiplier.Resolve(BaseTemplate != null ? BaseTemplate.ResolvedReflectSpeedMultiplier : 1f);
    public float ResolvedDashDamage => DashDamage.Resolve(BaseTemplate != null ? BaseTemplate.ResolvedDashDamage : 0f);
    public float ResolvedKnockbackChainDamage => KnockbackChainDamage.Resolve(BaseTemplate != null ? BaseTemplate.ResolvedKnockbackChainDamage : 0f);
    public float ResolvedKnockbackChainRadius => KnockbackChainRadius.Resolve(BaseTemplate != null ? BaseTemplate.ResolvedKnockbackChainRadius : 0f);
}
