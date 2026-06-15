using System;
using _System.Settings;
using PrimeTween;
using UnityEngine;

/// <summary>
/// Slides a UI panel in from / out to a screen edge by animating its <see cref="RectTransform"/>
/// anchored position (no fade, no scale change). The on-screen position is the panel's authored
/// position, captured on first init; the off-screen position is that position pushed past the
/// chosen edge by the panel's own size plus padding (or an explicit override).
///
/// Tweens use unscaled time so panels animate even while the game is paused (Time.timeScale = 0).
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class UISlidePanel : MonoBehaviour, IUIPanelAnimator
{
    public enum SlideEdge { Left, Right, Top, Bottom }

    [Tooltip("Panel to slide. Defaults to this object's own RectTransform.")]
    [SerializeField] private RectTransform _panel;

    [Tooltip("Screen edge the panel slides in from / out to.")]
    [SerializeField] private SlideEdge _slideFrom = SlideEdge.Left;

    [Tooltip("Extra off-screen travel past the edge, in pixels (clearance for shadows / glow).")]
    [SerializeField] private float _offScreenPadding = 100f;

    [Tooltip("Optional explicit off-screen offset relative to the on-screen position. " +
             "When non-zero it overrides the edge-based computation.")]
    [SerializeField] private Vector2 _offScreenOffsetOverride = Vector2.zero;

    [Tooltip("Slide duration override (seconds). <= 0 uses CpUISettings.PanelSlideDuration.")]
    [SerializeField] private float _durationOverride = -1f;

    private Vector2 _onScreenPos;
    private Vector2 _offScreenPos;
    private Tween _active;
    private bool _initialized;

    private RectTransform Panel => _panel != null ? _panel : (_panel = (RectTransform)transform);

    private float Duration => _durationOverride > 0f ? _durationOverride : CpUISettings.PanelSlideDuration;

    private void Awake() => EnsureInitialized();

    private void EnsureInitialized()
    {
        if (_initialized)
            return;
        _initialized = true;

        _onScreenPos = Panel.anchoredPosition;
        _offScreenPos = _onScreenPos + ComputeOffScreenOffset();
    }

    private Vector2 ComputeOffScreenOffset()
    {
        if (_offScreenOffsetOverride != Vector2.zero)
            return _offScreenOffsetOverride;

        Rect rect = Panel.rect;
        switch (_slideFrom)
        {
            case SlideEdge.Left:   return new Vector2(-(rect.width + _offScreenPadding), 0f);
            case SlideEdge.Right:  return new Vector2(  rect.width + _offScreenPadding,  0f);
            case SlideEdge.Top:    return new Vector2(0f,   rect.height + _offScreenPadding);
            case SlideEdge.Bottom: return new Vector2(0f, -(rect.height + _offScreenPadding));
            default:               return Vector2.zero;
        }
    }

    /// <summary>Slides the panel in from its off-screen edge to its authored position.</summary>
    public Tween Show()
    {
        EnsureInitialized();

        if (_active.isAlive)
            _active.Stop();

        // Don't tween an inactive target (PrimeTween warns). Snap to the on-screen position so the
        // panel is correct whenever it does become visible (e.g. a detail panel shown on selection).
        if (!Panel.gameObject.activeInHierarchy)
        {
            Panel.anchoredPosition = _onScreenPos;
            return default;
        }

        Panel.anchoredPosition = _offScreenPos;
        _active = Tween.UIAnchoredPosition(
            Panel, _onScreenPos, Duration, CpUISettings.PanelSlideInEase, useUnscaledTime: true);
        return _active;
    }

    /// <summary>
    /// Slides the panel off-screen, then invokes <paramref name="onComplete"/> (e.g. deactivate the
    /// GameObject). If no panel is animated the callback still runs.
    /// </summary>
    public Tween Hide(Action onComplete = null)
    {
        EnsureInitialized();

        if (_active.isAlive)
            _active.Stop();

        // Don't tween an inactive target (PrimeTween warns). Snap off-screen and finish immediately.
        if (!Panel.gameObject.activeInHierarchy)
        {
            Panel.anchoredPosition = _offScreenPos;
            onComplete?.Invoke();
            return default;
        }

        _active = Tween.UIAnchoredPosition(
            Panel, _offScreenPos, Duration, CpUISettings.PanelSlideOutEase, useUnscaledTime: true);

        if (onComplete != null)
            _active.OnComplete(onComplete);

        return _active;
    }
}
