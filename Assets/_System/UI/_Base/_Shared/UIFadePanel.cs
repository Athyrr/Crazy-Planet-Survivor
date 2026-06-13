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

    private Tween _active;

    private CanvasGroup Group =>
        _canvasGroup != null ? _canvasGroup : (_canvasGroup = GetComponent<CanvasGroup>());

    private float Duration => _durationOverride > 0f ? _durationOverride : CpUISettings.PanelSlideDuration;

    /// <summary>Fades the panel in from fully transparent to fully opaque.</summary>
    public Tween Show()
    {
        if (_active.isAlive)
            _active.Stop();

        Group.alpha = 0f;
        _active = Tween.Alpha(Group, 1f, Duration, CpUISettings.PanelSlideInEase, useUnscaledTime: true);
        return _active;
    }

    /// <summary>Fades the panel out, then invokes <paramref name="onComplete"/> (e.g. deactivate).</summary>
    public Tween Hide(Action onComplete = null)
    {
        if (_active.isAlive)
            _active.Stop();

        _active = Tween.Alpha(Group, 0f, Duration, CpUISettings.PanelSlideOutEase, useUnscaledTime: true);

        if (onComplete != null)
            _active.OnComplete(onComplete);

        return _active;
    }
}
