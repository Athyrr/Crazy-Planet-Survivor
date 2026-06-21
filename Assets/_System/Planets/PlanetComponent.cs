using UnityEngine;
using UnityEngine.EventSystems;

/// <summary> A selectable planet in the planet-selection view. /// </summary>
[RequireComponent(typeof(Collider))]
public class PlanetComponent : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    public EPlanetID PlanetID;

    public Vector3 FocusOffset;

    [Header("Animation")] public bool AllowRotate = false;
    public float IdleSpeed = 10f;
    public float SelectedSpeed = 50f;
    public float HoverScaleMult = 1.3f;
    public float SelectedScaleMult = 1.8f;
    public float ScaleSpeed = 5f;

    private PlanetSelectionUIController _controller;
    private bool _isSelected = false;
    private bool _isHovered = false;

    private Vector3 _baseScale;
    private Vector3 _targetScale;
    private float _currentSpeed;

    // Outline shown only on the playable planets while the planet-selection view is open.
    private Outline _outline;

    /// <summary>Only real, selectable planets are playable (decorative planets carry PlanetID.None / Lobby).</summary>
    private bool IsPlayable => PlanetID != EPlanetID.None && PlanetID != EPlanetID.Lobby;

    private void Awake()
    {
        _controller = FindFirstObjectByType<PlanetSelectionUIController>();
        _baseScale = transform.localScale;
        _targetScale = _baseScale;
        _currentSpeed = IdleSpeed;

        _outline = GetComponent<Outline>();
        if (_outline != null)
            _outline.enabled = false;
    }

    private void OnEnable()
    {
        if (_controller == null)
            _controller = FindFirstObjectByType<PlanetSelectionUIController>();

        if (_controller != null)
        {
            _controller.OnPlanetSelected += HandleSelectionChanged;
            _controller.OnPlanetHovered += HandleHoverChanged;
        }

        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnGameStateChanged += HandleGameStateChanged;
            ApplyOutline(GameManager.Instance.GetGameState());
        }

        _baseScale = transform.localScale;
        _targetScale = _baseScale;
        UpdateVisual();
    }

    private void OnDisable()
    {
        if (_controller != null)
        {
            _controller.OnPlanetSelected -= HandleSelectionChanged;
            _controller.OnPlanetHovered -= HandleHoverChanged;
        }

        if (GameManager.Instance != null)
            GameManager.Instance.OnGameStateChanged -= HandleGameStateChanged;
    }

    private void HandleGameStateChanged(EGameState newState) => ApplyOutline(newState);

    /// <summary>Outline is on only for playable planets while the planet-selection view is open.</summary>
    private void ApplyOutline(EGameState state)
    {
        if (_outline == null)
            return;

        bool shouldOutline = IsPlayable && state == EGameState.PlanetSelection;
        if (_outline.enabled != shouldOutline)
            _outline.enabled = shouldOutline;
    }

    private void HandleSelectionChanged(EPlanetID planetID, Transform planetTransform, Vector3 focusOffset)
    {
        _isSelected = planetID == PlanetID;
        UpdateVisual();
    }

    private void HandleHoverChanged(EPlanetID planetID)
    {
        _isHovered = planetID == PlanetID;
        UpdateVisual();
    }

    private void UpdateVisual()
    {
        if (_isSelected)
        {
            _targetScale = _baseScale * SelectedScaleMult;
            _currentSpeed = SelectedSpeed;
        }
        else if (_isHovered)
        {
            _targetScale = _baseScale * HoverScaleMult;
            _currentSpeed = IdleSpeed;
        }
        else
        {
            _targetScale = _baseScale;
            _currentSpeed = IdleSpeed;
        }
    }

    private void Update()
    {
        if (AllowRotate)
            transform.Rotate(transform.up * Time.deltaTime * _currentSpeed);

        Vector3 current = transform.localScale;
        if ((current - _targetScale).sqrMagnitude > 0.0001f)
            transform.localScale = Vector3.Lerp(current, _targetScale, Time.deltaTime * ScaleSpeed);
        else
            transform.localScale = _targetScale;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!IsSelecting())
            return;

        _controller?.HoverPlanet(PlanetID);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (!IsSelecting())
            return;

        _controller?.ClearHover(PlanetID);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (!IsSelecting())
            return;

        _controller?.ClickPlanet(this);
    }

    private static bool IsSelecting() =>
        GameManager.Instance != null && GameManager.Instance.GetGameState() == EGameState.PlanetSelection;
}