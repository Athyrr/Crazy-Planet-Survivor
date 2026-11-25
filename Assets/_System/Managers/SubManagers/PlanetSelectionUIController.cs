using System.Collections.Generic;
using System.Linq;
using _System.Settings;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class PlanetSelectionUIController : MonoBehaviour
{
    public delegate void OnPlanetSelectedDelegate(PlanetSO planet, Transform planetTransform, Vector3 focusOffset);
    public delegate void OnPlanetHoveredDelegate(PlanetSO planet);

    /// <summary>Fired when the committed (focused) planet changes (null = none). Drives camera focus + select visual.</summary>
    public event OnPlanetSelectedDelegate OnPlanetSelected = null;

    /// <summary>Fired when the highlighted (hovered) planet changes (null = none). Drives the hover visual only.</summary>
    public event OnPlanetHoveredDelegate OnPlanetHovered = null;

    public Button ExploreButton;

    private static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
    private Material _exploreOutlineMat;

    public Button BackButton;

    // null = no planet focused / hovered.
    private PlanetSO _focusedPlanet;
    private PlanetSO _hoveredPlanet;

    private GameInputs _inputs;
    private List<PlanetComponent> _planets = new();
    private bool _navAxisActive;

    private void Awake()
    {
        _focusedPlanet = null;
        _hoveredPlanet = null;
    }

    private void OnEnable()
    {
        ExploreButton.gameObject.SetActive(false);
        SetExploreOutlineFocused(false);
        if (BackButton != null)
            BackButton.gameObject.SetActive(false);

        if (_inputs == null)
            _inputs = new GameInputs();

        _inputs.UI.Navigate.performed += OnNavigate;
        _inputs.UI.Cancel.performed += OnCancel;
        _inputs.UI.Enable();
        
        _inputs.Player.Interact.performed += OnConfirm;
        _inputs.Player.Interact.Enable();

        RefreshPlanets();
    }

    private void OnDisable()
    {
        ExploreButton.gameObject.SetActive(false);
        SetExploreOutlineFocused(false);
        if (BackButton != null)
            BackButton.gameObject.SetActive(false);
        _focusedPlanet = null;
        _hoveredPlanet = null;

        if (_inputs != null)
        {
            _inputs.UI.Navigate.performed -= OnNavigate;
            _inputs.UI.Cancel.performed -= OnCancel;
            _inputs.UI.Disable();

            _inputs.Player.Interact.performed -= OnConfirm;
            _inputs.Player.Interact.Disable();
        }
    }

    public PlanetSO SelectedPlanet => _focusedPlanet;
    public PlanetSO HoveredPlanet => _hoveredPlanet;


    /// <summary>
    /// Highlights a planet (controller navigation, or mouse/touch pointer-enter).
    /// Locked while a planet is focused — you must unfocus (Cancel) before hovering another.
    /// </summary>
    public void HoverPlanet(PlanetSO planet)
    {
        if (_focusedPlanet != null)
            return;

        if (_hoveredPlanet == planet)
            return;

        _hoveredPlanet = planet;
        OnPlanetHovered?.Invoke(_hoveredPlanet);
    }

    /// <summary>Clears the hover when the pointer leaves the hovered planet (ignored while focused).</summary>
    public void ClearHover(PlanetSO planet)
    {
        if (_focusedPlanet != null)
            return;

        if (_hoveredPlanet != planet)
            return;

        _hoveredPlanet = null;
        
        // BackButton.gameObject.SetActive(true);
        
        OnPlanetHovered?.Invoke(null);
    }

    // ---------- Focus (committed selection) ----------

    public void SelectPlanet(PlanetSO planet, Transform planetTransform = null, Vector3 focusOffset = default)
    {
        if (_focusedPlanet == planet)
        {
            // Toggle off.
            _focusedPlanet = null;
            planetTransform = null;
        }
        else
        {
            _focusedPlanet = planet;
        }

        bool hasSelection = _focusedPlanet != null;
        ExploreButton.gameObject.SetActive(hasSelection);
        SetExploreOutlineFocused(hasSelection);

        if (hasSelection)
            Debug.Log($"[Planet Selection] Focused Planet: {_focusedPlanet.name}");

        BackButton.gameObject.SetActive(!hasSelection);
        
        OnPlanetSelected?.Invoke(_focusedPlanet, planetTransform, focusOffset);
    }

    /// <summary>Mouse/touch tap on a planet. While focused, only the focused planet responds (toggles off).</summary>
    public void ClickPlanet(PlanetComponent planet)
    {
        // Scenery planets (no PlanetSO) are not selectable.
        if (planet == null || planet.Planet == null)
            return;

        // Locked to the focused planet — tapping another is ignored until you unfocus.
        if (_focusedPlanet != null && planet.Planet != _focusedPlanet)
            return;

        HoverPlanet(planet.Planet);
        SelectPlanet(planet.Planet, planet.transform, planet.FocusOffset);
    }

    /// <summary>Confirm input: focus the hovered planet, or launch the run if it is already focused.</summary>
    public void ConfirmHovered()
    {
        if (_hoveredPlanet == null)
            return;

        if (_focusedPlanet == _hoveredPlanet)
        {
            ExplorePlanet();
            return;
        }

        var planet = FindPlanet(_hoveredPlanet);
        if (planet != null)
            SelectPlanet(planet.Planet, planet.transform, planet.FocusOffset);
    }

    public void ExplorePlanet()
    {
        if (_focusedPlanet == null)
            return;

        // Capture and clear first so a double confirm/click can't launch twice.
        var planet = _focusedPlanet;
        _focusedPlanet = null;
        ExploreButton.gameObject.SetActive(false);
        SetExploreOutlineFocused(false);
        if (BackButton != null)
            BackButton.gameObject.SetActive(false);

        GameManager.Instance.StartRun(planet);
    }


    public void BackToLobby()
    {
        if (!IsSelecting())
            return;

        _focusedPlanet = null;
        _hoveredPlanet = null;
        ExploreButton.gameObject.SetActive(false);
        SetExploreOutlineFocused(false);
        if (BackButton != null)
            BackButton.gameObject.SetActive(false);

        OnPlanetSelected?.Invoke(null, null, default);
        OnPlanetHovered?.Invoke(null);

        GameManager.Instance.ChangeState(EGameState.Lobby);
    }

    public void OpenView()
    {
        _focusedPlanet = null;
        _hoveredPlanet = null;
        ExploreButton.gameObject.SetActive(false);
        SetExploreOutlineFocused(false);

        // Back is always available while the galaxy is open.
        if (BackButton != null)
            BackButton.gameObject.SetActive(true);

        RefreshPlanets();

        OnPlanetSelected?.Invoke(null, null, default);
        OnPlanetHovered?.Invoke(null);
    }

    public void CloseView()
    {
        ExploreButton.gameObject.SetActive(false);
        SetExploreOutlineFocused(false);
        if (BackButton != null)
            BackButton.gameObject.SetActive(false);
        _focusedPlanet = null;
        _hoveredPlanet = null;
    }

    /// <summary>
    /// Drives the Explore button image's UI outline shader so the button reads as one selected unit
    /// with the committed planet (_OutlineColor = CpUISettings.ItemOutlineSelected while a planet
    /// is committed, transparent otherwise). The button material is cloned on first use so we don't
    /// repaint other UI graphics that share the same SGI_UI_ElementOutline asset (e.g. the Back button).
    /// </summary>
    private void SetExploreOutlineFocused(bool focused)
    {
        if (ExploreButton == null || ExploreButton.image == null || ExploreButton.image.material == null)
            return;

        if (_exploreOutlineMat == null)
        {
            _exploreOutlineMat = new Material(ExploreButton.image.material);
            ExploreButton.image.material = _exploreOutlineMat;
        }

        Color outlineColor = CpUISettings.ItemOutlineSelected;
        if (!focused)
            outlineColor.a = 0f;
        _exploreOutlineMat.SetColor(OutlineColorId, outlineColor);
    }

    // ---------- Internals ----------

    /// <summary>Planets can only be interacted with while the planet-selection view is open.</summary>
    private static bool IsSelecting() =>
        GameManager.Instance != null && GameManager.Instance.GetGameState() == EGameState.PlanetSelection;


    /// <summary>Collects the selectable planets (those with a PlanetSO) in the scene, ordered left-to-right.</summary>
    private void RefreshPlanets()
    {
        _planets = FindObjectsByType<PlanetComponent>(FindObjectsSortMode.None)
            .Where(p => p.Planet != null)
            .OrderBy(p => p.transform.position.x)
            .ToList();
    }

    private PlanetComponent FindPlanet(PlanetSO planet) => _planets.FirstOrDefault(p => p.Planet == planet);

    private void OnNavigate(InputAction.CallbackContext ctx)
    {
        if (!IsSelecting())
            return;

        // Hover is locked while a planet is focused — unfocus (Cancel) first.
        if (_focusedPlanet != null)
            return;

        if (_planets.Count == 0)
            RefreshPlanets();
        if (_planets.Count == 0)
            return;

        float x = ctx.ReadValue<Vector2>().x;

        // Edge-triggered: one step per stick push / key press (release to step again).
        if (Mathf.Abs(x) < 0.3f)
        {
            _navAxisActive = false;
            return;
        }

        if (_navAxisActive)
            return;
        _navAxisActive = true;

        int step = x > 0 ? 1 : -1;
        int n = _planets.Count;
        int current = _planets.FindIndex(p => p.Planet == _hoveredPlanet);
        int start = current < 0 ? (step > 0 ? -1 : 0) : current;
        int next = ((start + step) % n + n) % n;

        HoverPlanet(_planets[next].Planet);
    }

    private void OnConfirm(InputAction.CallbackContext ctx)
    {
        if (!IsSelecting())
            return;

        ConfirmHovered();
    }

    private void OnCancel(InputAction.CallbackContext ctx)
    {
        if (!IsSelecting())
            return;

        // A planet is focused → drop the focus but keep the hover (navigation can continue from here).
        if (_focusedPlanet != null)
        {
            SelectPlanet(_focusedPlanet); // same planet → toggles the focus off
            return;
        }

        // Nothing focused → leave the galaxy and return to the lobby.
        BackToLobby();
    }
}
