using System;
using _System.Settings;
using PrimeTween;
using UnityEngine;

/// <summary>
/// Fades a UI element in/out via its <see cref="CanvasGroup"/> alpha (no movement). Use for backdrops
/// / dim overlays that should appear without sliding. Uses unscaled time so it plays while paused.
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class UIFadePanel : MonoBehaviour, IUIPanelAnimator
{
    [SerializeField] private CanvasGroup _canvasGroup;

    [Tooltip("Fade duration override (seconds). <= 0 uses CpUISettings.PanelSlideDuration.")]
    [SerializeField] private float _durationOverride = -1f;

    [Tooltip("Play the fade-in automatically whenever this object is enabled. Tick this for panels with " +
             "no controller calling Show() (e.g. standalone HUD widgets). Leave off when a controller " +
             "drives Show()/Hide() itself.")]
    [SerializeField] private bool _playOnEnable;

    [Tooltip("Seconds to wait before the fade actually starts (the panel stays fully transparent / opaque " +
             "during the wait). Use it to stagger panels that would otherwise overlap. <= 0 = no delay.")]
    [SerializeField] private float _delayBeforePlay = -1f;

    private Tween _active;

    // Self-drives the fade-in when enabled (plug-and-play). Safe here because a component's own OnEnable
    // only runs once its GameObject is already active in the hierarchy — no activation-order race.
    private void OnEnable()
    {
        if (_playOnEnable)
            Show();
    }

    private CanvasGroup Group =>
        _canvasGroup != null ? _canvasGroup : (_canvasGroup = GetComponent<CanvasGroup>());

    private float Duration => _durationOverride > 0f ? _durationOverride : CpUISettings.PanelSlideDuration;

    private float DelayBeforePlay => _delayBeforePlay > 0f ? _delayBeforePlay : 0f;

    /// <summary>Fades the panel in from fully transparent to fully opaque.</summary>
    public Tween Show()
    {
        if (_active.isAlive)
            _active.Stop();

        // Don't tween an inactive target (PrimeTween warns). Snap to opaque so it's correct when shown.
        if (!Group.gameObject.activeInHierarchy)
        {
            Group.alpha = 1f;
            return default;
        }

        Group.alpha = 0f;
        _active = Tween.Alpha(Group, 1f, Duration, CpUISettings.PanelSlideInEase,
            startDelay: DelayBeforePlay, useUnscaledTime: true);
        return _active;
    }

    /// <summary>Fades the panel out, then invokes <paramref name="onComplete"/> (e.g. deactivate).</summary>
    public Tween Hide(Action onComplete = null)
    {
        if (_active.isAlive)
            _active.Stop();

        // Don't tween an inactive target (PrimeTween warns). Snap transparent and finish immediately.
        if (!Group.gameObject.activeInHierarchy)
        {
            Group.alpha = 0f;
            onComplete?.Invoke();
            return default;
        }

        _active = Tween.Alpha(Group, 0f, Duration, CpUISettings.PanelSlideOutEase,
            startDelay: DelayBeforePlay, useUnscaledTime: true);

        if (onComplete != null)
            _active.OnComplete(onComplete);

        return _active;
    }
}
