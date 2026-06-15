using _System.Settings;
using PrimeTween;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Generic hover/focus feedback for an interactive UI element: scale-pops on pointer enter or
/// EventSystem selection and returns to its captured base scale on exit/deselect. Uses unscaled
/// time so it works while the game is paused. Attach to standalone buttons (Back, menu, settings
/// rows). Shop list items drive their own scale through the item's hover/focus state instead.
/// </summary>
public class UIButtonHover : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
{
    [Tooltip("Hover scale multiplier override. <= 0 uses CpUISettings.HoverScale.")]
    [SerializeField] private float _hoverScaleOverride = -1f;

    private Vector3 _baseScale = Vector3.one;
    private Tween _tween;
    private bool _captured;

    private float HoverScale => _hoverScaleOverride > 0f ? _hoverScaleOverride : CpUISettings.HoverScale;

    private void Awake()
    {
        _baseScale = transform.localScale;
        _captured = true;
    }

    private void OnDisable()
    {
        // Never leave a re-enabled button stuck at hover scale.
        if (_tween.isAlive)
            _tween.Stop();
        if (_captured)
            transform.localScale = _baseScale;
    }

    public void OnPointerEnter(PointerEventData eventData) => AnimateTo(_baseScale * HoverScale);
    public void OnPointerExit(PointerEventData eventData) => AnimateTo(_baseScale);
    public void OnSelect(BaseEventData eventData) => AnimateTo(_baseScale * HoverScale);
    public void OnDeselect(BaseEventData eventData) => AnimateTo(_baseScale);

    private void AnimateTo(Vector3 targetScale)
    {
        if (_tween.isAlive)
            _tween.Stop();

        // Skip a no-op tween when already at the target (PrimeTween warns on equal end value).
        if (transform.localScale == targetScale)
            return;

        _tween = Tween.Scale(
            transform, targetScale, CpUISettings.HoverDuration, CpUISettings.HoverEase, useUnscaledTime: true);
    }
}
