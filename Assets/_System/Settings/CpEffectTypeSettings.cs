using System;
using UnityEngine;
using _System.Settings;

[CreateAssetMenu(fileName = "NewCpEffectTypeSettings", menuName = "CPSettings/EffectTypeSettings")]
public class CpEffectTypeSettings : CpSettings<CpEffectTypeSettings>
{
    [Serializable]
    public struct Entry
    {
        public EffectType Type;

        [Tooltip("Burn only: ratio applied to the triggering hit's pre-crit damage.")]
        public float Ratio;

        [Tooltip("Slow/Stun/Knockback: flat base magnitude (speed-reduction multiplier / unused / push force). Burn: unused.")]
        public float BaseMagnitude;

        public float BaseDuration;

        [Tooltip("Burn only — ignored by instant-apply types.")]
        public float TickRate;

        public EStackMode StackMode;

        [Tooltip("Only used when StackMode == StackCapped.")]
        public int MaxStacks;

        [Tooltip("Off by default — preserves current behavior. Also upgradeable at runtime (CoreStats.BurnCanCrit).")]
        public bool AllowCrit;

        [Tooltip("Off by default — preserves current behavior. Also upgradeable at runtime (CoreStats.BurnCanLifeSteal).")]
        public bool AllowLifeSteal;
    }

    public Entry[] Entries =
    {
        new Entry { Type = EffectType.Burn,      Ratio = 0.125f, BaseDuration = 3f,   TickRate = 0.3f, StackMode = EStackMode.RefreshOnly },
        new Entry { Type = EffectType.Slow,      BaseMagnitude = 2f,  BaseDuration = 3f,   StackMode = EStackMode.RefreshOnly },
        new Entry { Type = EffectType.Stun,      BaseDuration = 1.5f, StackMode = EStackMode.RefreshOnly },
        new Entry { Type = EffectType.Knockback, BaseMagnitude = 90f, BaseDuration = 0.7f, StackMode = EStackMode.RefreshOnly },
    };

    [Header("Knockback force falloff (shared across all knockback instances)")]
    [Tooltip("X = normalized elapsed time (0 = impact, 1 = end). Y = fraction of the entry's BaseMagnitude applied.")]
    public AnimationCurve KnockbackForceCurve = new AnimationCurve(
        new Keyframe(0.00f, 1.00f, 0f, 0f),
        new Keyframe(0.10f, 0.85f, 0f, 0f),
        new Keyframe(0.35f, 0.30f, 0f, 0f),
        new Keyframe(1.00f, 0.00f, 0f, 0f));

    [Range(8, 128)] public int KnockbackCurveResolution = 32;

    [Header("Life Steal")]
    [Tooltip("Fraction of a hit's damage healed on a successful life-steal proc. 0.075 = 7.5%.")]
    public float LifeStealConversion = 0.075f;

    [Tooltip("Minimum seconds between two life-steal procs (rate limiter). 0.1 = max 10 procs/s.")]
    public float LifeStealProcCooldown = 0.1f;
}
