using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class PlanetSelectionUIController : MonoBehaviour
{
    public delegate void OnPlanetSelectedDelegate(EPlanetID planetID, Transform planetTransform, Vector3 focusOffset);
    public delegate void OnPlanetHoveredDelegate(EPlanetID planetID);

    /// <summary>Fired when the committed (focused) planet changes. Drives camera focus + select visual.</summary>
    public event OnPlanetSelectedDelegate OnPlanetSelected = null;

    /// <summary>Fired when the highlighted (hovered) planet changes. Drives the hover visual only.</summary>
    public event OnPlanetHoveredDelegate OnPlanetHovered = null;

    public Button ExploreButton;
    
    public Button BackButton;

    private EPlanetID _focusedPlanet;
    private EPlanetID _hoveredPlanet;   

    private GameInputs _inputs;
    private List<PlanetComponent> _planets = new();
    private bool _navAxisActive;

    private void Awake()
    {
        _focusedPlanet = EPlanetID.None;
        _hoveredPlanet = EPlanetID.None;
    }

    private void OnEnable()
    {
        ExploreButton.gameObject.SetActive(false);
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
        if (BackButton != null)
            BackButton.gameObject.SetActive(false);
        _focusedPlanet = EPlanetID.None;
        _hoveredPlanet = EPlanetID.None;

        if (_inputs != null)
        {
            _inputs.UI.Navigate.performed -= OnNavigate;
            _inputs.UI.Cancel.performed -= OnCancel;
            _inputs.UI.Disable();

            _inputs.Player.Interact.performed -= OnConfirm;
            _inputs.Player.Interact.Disable();
        }
    }

    public EPlanetID SelectedPlanet => _focusedPlanet;
    public EPlanetID HoveredPlanet => _hoveredPlanet;


    /// <summary>
    /// Highlights a planet (controller navigation, or mouse/touch pointer-enter).
    /// Locked while a planet is focused — you must unfocus (Cancel) before hovering another.
    /// </summary>
    public void HoverPlanet(EPlanetID planetID)
    {
        if (_focusedPlanet != EPlanetID.None)
            return;

        if (_hoveredPlanet == planetID)
            return;

        _hoveredPlanet = planetID;
        OnPlanetHovered?.Invoke(_hoveredPlanet);
    }

    /// <summary>Clears the hover when the pointer leaves the hovered planet (ignored while focused).</summary>
    public void ClearHover(EPlanetID planetID)
    {
        if (_focusedPlanet != EPlanetID.None)
            return;

        if (_hoveredPlanet != planetID)
            return;

        _hoveredPlanet = EPlanetID.None;
        OnPlanetHovered?.Invoke(EPlanetID.None);
    }

    // ---------- Focus (committed selection) ----------

    public void SelectPlanet(EPlanetID planetID, Transform planetTransform = null, Vector3 focusOffset = default)
    {
        if (_focusedPlanet == planetID)
        {
            // Toggle off.
            _focusedPlanet = EPlanetID.None;
            planetTransform = null;
        }
        else
        {
            _focusedPlanet = planetID;
        }

        bool hasSelection = _focusedPlanet != EPlanetID.None;
        ExploreButton.gameObject.SetActive(hasSelection);

        if (hasSelection)
            Debug.Log($"[Planet Selection] Focused Planet: {_focusedPlanet}");

        OnPlanetSelected?.Invoke(_focusedPlanet, planetTransform, focusOffset);
    }

    /// <summary>Mouse/touch tap on a planet. While focused, only the focused planet responds (toggles off).</summary>
    public void ClickPlanet(PlanetComponent planet)
    {
        if (planet == null)
            return;

        // Locked to the focused planet — tapping another is ignored until you unfocus.
        if (_focusedPlanet != EPlanetID.None && planet.PlanetID != _focusedPlanet)
            return;

        HoverPlanet(planet.PlanetID);
        SelectPlanet(planet.PlanetID, planet.transform, planet.FocusOffset);
    }

    /// <summary>Confirm input: focus the hovered planet, or launch the run if it is already focused.</summary>
    public void ConfirmHovered()
    {
        if (_hoveredPlanet == EPlanetID.None)
            return;

        if (_focusedPlanet == _hoveredPlanet)
        {
            ExplorePlanet();
            return;
        }

        var planet = FindPlanet(_hoveredPlanet);
        if (planet != null)
            SelectPlanet(planet.PlanetID, planet.transform, planet.FocusOffset);
    }

    public void ExplorePlanet()
    {
        if (_focusedPlanet == EPlanetID.None)
            return;

        // Capture and clear first so a double confirm/click can't launch twice.
        var planet = _focusedPlanet;
        _focusedPlanet = EPlanetID.None;
        ExploreButton.gameObject.SetActive(false);
        if (BackButton != null)
            BackButton.gameObject.SetActive(false);

        GameManager.Instance.StartRun(planet);
    }


    public void BackToLobby()
    {
        if (!IsSelecting())
            return;

        _focusedPlanet = EPlanetID.None;
        _hoveredPlanet = EPlanetID.None;
        ExploreButton.gameObject.SetActive(false);
        if (BackButton != null)
            BackButton.gameObject.SetActive(false);

        OnPlanetSelected?.Invoke(EPlanetID.None, null, default);
        OnPlanetHovered?.Invoke(EPlanetID.None);

        GameManager.Instance.ChangeState(EGameState.Lobby);
    }

    public void OpenView()
    {
        _focusedPlanet = EPlanetID.None;
        _hoveredPlanet = EPlanetID.None;
        ExploreButton.gameObject.SetActive(false);

        // Back is always available while the galaxy is open.
        if (BackButton != null)
            BackButton.gameObject.SetActive(true);

        RefreshPlanets();

        OnPlanetSelected?.Invoke(EPlanetID.None, null, default);
        OnPlanetHovered?.Invoke(EPlanetID.None);
    }

    public void CloseView()
    {
        ExploreButton.gameObject.SetActive(false);
        if (BackButton != null)
            BackButton.gameObject.SetActive(false);
        _focusedPlanet = EPlanetID.None;
        _hoveredPlanet = EPlanetID.None;
    }

    // ---------- Internals ----------

    /// <summary>Planets can only be interacted with while the planet-selection view is open.</summary>
    private static bool IsSelecting() =>
        GameManager.Instance != null && GameManager.Instance.GetGameState() == EGameState.PlanetSelection;


    /// <summary>Collects the selectable planets in the scene, ordered left-to-right.</summary>
    private void RefreshPlanets()
    {
        _planets = FindObjectsByType<PlanetComponent>(FindObjectsSortMode.None)
            .Where(p => p.PlanetID != EPlanetID.None && p.PlanetID != EPlanetID.Lobby)
            .OrderBy(p => p.transform.position.x)
            .ToList();
    }

    private PlanetComponent FindPlanet(EPlanetID id) => _planets.FirstOrDefault(p => p.PlanetID == id);

    private void OnNavigate(InputAction.CallbackContext ctx)
    {
        if (!IsSelecting())
            return;

        // Hover is locked while a planet is focused — unfocus (Cancel) first.
        if (_focusedPlanet != EPlanetID.None)
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
        int current = _planets.FindIndex(p => p.PlanetID == _hoveredPlanet);
        int start = current < 0 ? (step > 0 ? -1 : 0) : current;
        int next = ((start + step) % n + n) % n;

        HoverPlanet(_planets[next].PlanetID);
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
        if (_focusedPlanet != EPlanetID.None)
        {
            SelectPlanet(_focusedPlanet); // same id → toggles the focus off
            return;
        }

        // Nothing focused → leave the galaxy and return to the lobby.
        BackToLobby();
    }
}
