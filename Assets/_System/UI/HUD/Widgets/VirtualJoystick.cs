using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// On-screen virtual joystick for touch / mobile. Reports a normalized [-1,1] direction
/// that <see cref="PlayerController"/> reads as movement input (via SetVirtualMoveInput).
/// Shown only on touch-capable platforms by default; inert on desktop/console.
///
/// Setup: place this on a UI object that has a raycast-target Graphic (e.g. an Image) acting
/// as the joystick <see cref="Background"/>, with a child Image as the <see cref="Handle"/>.
/// Both should use a centered pivot/anchor. Drop it on the HUD Canvas.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    [Header("References")]
    [Tooltip("The static ring / background of the joystick. Must have a raycast-target Graphic.")]
    public RectTransform Background;

    [Tooltip("The draggable knob; moves within the Background. Optional (visual only).")]
    public RectTransform Handle;

    [Header("Tuning")]
    [Range(0f, 0.9f)]
    [Tooltip("Inputs below this magnitude are treated as zero.")]
    public float DeadZone = 0.1f;

    [Header("Behaviour")]
    [Tooltip("Only show / activate on touch-capable platforms. Disable to test with a mouse in the editor.")]
    public bool TouchPlatformsOnly = true;

    /// <summary>Current normalized joystick direction (zero when released).</summary>
    public Vector2 Direction { get; private set; }

    private PlayerController _controller;

    private void Awake()
    {
        if (Background == null)
            Background = transform as RectTransform;

        if (TouchPlatformsOnly && !IsTouchPlatform())
        {
            gameObject.SetActive(false);
            return;
        }

        ResetHandle();
    }

    public void OnPointerDown(PointerEventData eventData) => OnDrag(eventData);

    public void OnDrag(PointerEventData eventData)
    {
        if (Background == null)
            return;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                Background, eventData.position, eventData.pressEventCamera, out Vector2 local))
            return;

        // Make the value relative to the rect's center, robust to any pivot.
        Vector2 size = Background.rect.size;
        Vector2 fromCenter = local - Background.rect.center;
        Vector2 normalized = new Vector2(
            size.x > 0f ? fromCenter.x / (size.x * 0.5f) : 0f,
            size.y > 0f ? fromCenter.y / (size.y * 0.5f) : 0f);

        if (normalized.sqrMagnitude > 1f)
            normalized = normalized.normalized;

        Direction = normalized.magnitude < DeadZone ? Vector2.zero : normalized;

        if (Handle != null)
            Handle.anchoredPosition = new Vector2(normalized.x * size.x * 0.5f, normalized.y * size.y * 0.5f);

        PushToController(Direction);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        Direction = Vector2.zero;
        ResetHandle();
        PushToController(Vector2.zero, released: true);
    }

    private void OnDisable()
    {
        // Don't leave the player sliding if the joystick is hidden/disabled mid-drag.
        Direction = Vector2.zero;
        ResetHandle();
        if (_controller != null)
            _controller.ClearVirtualMoveInput();
    }

    private void ResetHandle()
    {
        if (Handle != null)
            Handle.anchoredPosition = Vector2.zero;
    }

    private void PushToController(Vector2 direction, bool released = false)
    {
        if (_controller == null)
            _controller = FindFirstObjectByType<PlayerController>();

        if (_controller == null)
            return;

        if (released)
            _controller.ClearVirtualMoveInput();
        else
            _controller.SetVirtualMoveInput(direction);
    }

    private static bool IsTouchPlatform()
    {
#if UNITY_EDITOR
        // Always available in the editor so it can be tested with the mouse / Device Simulator.
        return true;
#else
        // Real touch device, or a touchscreen is connected.
        return Application.isMobilePlatform || Touchscreen.current != null;
#endif
    }
}
