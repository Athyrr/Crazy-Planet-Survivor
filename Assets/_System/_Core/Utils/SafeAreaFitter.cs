using UnityEngine;

/// <summary>
/// Drives a RectTransform to fit inside <see cref="Screen.safeArea"/> so that UI never lands
/// under a notch, Dynamic Island, punch-hole camera or the home indicator.
///
/// Usage: place this on a full-stretch RectTransform that is a direct child of a Canvas, then
/// parent the interactive UI (HUD widgets, menus, joystick...) under it. Full-bleed backgrounds
/// should stay at the Canvas root, outside the safe area, so they keep covering the whole screen.
///
/// Works with any CanvasScaler mode (it computes normalized anchors, so it is resolution
/// independent) and re-applies automatically on resolution / orientation / safe-area changes,
/// which is what makes it correct for landscape phones where the notch flips left/right.
/// </summary>
[RequireComponent(typeof(RectTransform))]
[DisallowMultipleComponent]
public class SafeAreaFitter : MonoBehaviour
{
    [Tooltip("Apply the left / right safe-area insets (the notch side in landscape).")]
    [SerializeField] private bool _conformX = true;

    [Tooltip("Apply the top / bottom safe-area insets (the home indicator in landscape).")]
    [SerializeField] private bool _conformY = true;

    private RectTransform _rect;
    private Rect _lastSafeArea = new Rect(0, 0, 0, 0);
    private Vector2Int _lastScreen = new Vector2Int(0, 0);
    private ScreenOrientation _lastOrientation = ScreenOrientation.AutoRotation;

    private void Awake()
    {
        _rect = GetComponent<RectTransform>();
        Apply();
    }

    private void Update()
    {
        // Cheap guard: only recompute when something actually changed.
        if (Screen.safeArea != _lastSafeArea
            || Screen.width != _lastScreen.x
            || Screen.height != _lastScreen.y
            || Screen.orientation != _lastOrientation)
        {
            Apply();
        }
    }

    private void Apply()
    {
        if (_rect == null) _rect = GetComponent<RectTransform>();

        Rect safe = Screen.safeArea;
        int w = Screen.width;
        int h = Screen.height;
        if (w <= 0 || h <= 0) return;

        Vector2 min = safe.position;
        Vector2 max = safe.position + safe.size;
        min.x /= w; min.y /= h;
        max.x /= w; max.y /= h;

        if (!_conformX) { min.x = 0f; max.x = 1f; }
        if (!_conformY) { min.y = 0f; max.y = 1f; }

        // Reject degenerate / out-of-range values (some platforms report a zero rect for a frame).
        if (min.x < 0f || min.y < 0f || max.x > 1f || max.y > 1f || min.x >= max.x || min.y >= max.y)
        {
            min = Vector2.zero;
            max = Vector2.one;
        }

        _rect.anchorMin = min;
        _rect.anchorMax = max;
        _rect.offsetMin = Vector2.zero;
        _rect.offsetMax = Vector2.zero;

        _lastSafeArea = safe;
        _lastScreen = new Vector2Int(w, h);
        _lastOrientation = Screen.orientation;
    }
}
