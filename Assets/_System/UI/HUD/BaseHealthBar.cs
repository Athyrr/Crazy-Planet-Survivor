using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Reusable health-bar view shared by every health bar (player, boss, elite). Renders the classic
/// "damage trail": <see cref="HealthFill"/> (front) snaps to the current ratio, while
/// <see cref="DamageFill"/> lerps down to reveal the loss.
/// </summary>
public class BaseHealthBar : MonoBehaviour
{
    [Header("Fills (Image type = Filled, Horizontal)")]
    [Tooltip("Front fill. Snaps instantly to the current health ratio.")]
    public Image HealthFill;

    [Tooltip("Trail fill drawn behind HealthFill. Lerps down after a hit.")]
    public Image DamageFill;

    [Header("Labels (optional)")]
    [Tooltip("Optional 'current / max' numeric label.")]
    public TMP_Text ValueText;

    [Tooltip("Optional name label (e.g. boss/elite name).")]
    public TMP_Text NameText;

    [Header("Trail animation")]
    [Tooltip("Fill units drained per second (1 = whole bar in 1s).")]
    public float DrainSpeed = 1.2f;

    [Tooltip("Seconds the trail holds after a hit before it starts draining.")]
    public float DrainDelay = 0.25f;

    protected float _target = 1f;
    private float _drainTimer;

    /// <summary> Sets the optional name label. </summary>
    public void SetName(string displayName)
    {
        if (NameText != null)
            NameText.text = displayName ?? string.Empty;
    }

    /// <summary> Sets the optional "current / max" numeric label. </summary>
    public void SetValue(int current, int max)
    {
        if (ValueText != null)
            ValueText.text = $"{current} / {max}";
    }

    /// <summary> Sets the current health ratio (0..1). The front fill snaps; the trail animates. </summary>
    public void SetHealth(float ratio)
    {
        ratio = Mathf.Clamp01(ratio);
        if (ratio < _target)
            _drainTimer = DrainDelay; // took damage: hold the trail before draining
        _target = ratio;
    }

    /// <summary> Snaps both fills to a ratio with no animation (use when (re)binding a pooled bar). </summary>
    public void ResetTo(float ratio)
    {
        ratio = Mathf.Clamp01(ratio);
        _target = ratio;
        _drainTimer = 0f;
        if (HealthFill != null) HealthFill.fillAmount = ratio;
        if (DamageFill != null) DamageFill.fillAmount = ratio;
    }

    protected virtual void Update()
    {
        TickTrail();
    }

    /// <summary> Advances the front fill (instant) and the trailing fill (delayed lerp) one frame. </summary>
    protected void TickTrail()
    {
        if (HealthFill != null)
            HealthFill.fillAmount = _target;

        if (DamageFill == null)
            return;

        // Heal (or in sync): keep the trail tight to the health, no animation.
        if (DamageFill.fillAmount <= _target)
        {
            DamageFill.fillAmount = _target;
            return;
        }

        if (_drainTimer > 0f)
        {
            _drainTimer -= Time.deltaTime;
            return;
        }

        DamageFill.fillAmount = Mathf.MoveTowards(DamageFill.fillAmount, _target, DrainSpeed * Time.deltaTime);
    }
}
