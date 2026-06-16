using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Controller for the in-run pause menu.
/// </summary>
public class PauseUIController : UIControllerBase, ISettingsControllerOwner
{
    [Header("Buttons (top to bottom)")] public Button ResumeButton;
    public Button AbandonButton;
    public Button OptionsButton;
    public Button QuitButton;

    [Header("Options / Settings")]
    [Tooltip("Settings sub-panel opened by the Options button. While it is open the menu buttons are " +
             "hidden and pause input is suspended; the panel returns control via OnSettingsClosed().")]
    public SettingsPanelController SettingsPanel;

    [Tooltip("The buttons list (hidden while the settings panel is open). Defaults to this object.")]
    public GameObject MenuButtonsRoot;

    private GameInputs _inputs;
    private Button[] _buttons;
    private int _focusedIndex = -1;
    private NavRepeatFilter _nav;
    private bool _prevSendNavEvents = true;

    // Index of the Options button in _buttons (Resume, Abandon, Options, Quit).
    private const int OptionsIndex = 2;

    private void Awake() => EnsureButtons();

    private void OnEnable()
    {
        // Play the panel's entrance animation (slide or fade — whichever IUIPanelAnimator is attached).
        // Uses unscaled time so it plays while paused.
        if (TryGetComponent<IUIPanelAnimator>(out var animator))
            animator.Show();

        EnsureButtons();
        WireButtons(true);
        EnableInput();

        // Always open the pause menu on the buttons page, never a leftover settings page.
        if (SettingsPanel != null)
            SettingsPanel.gameObject.SetActive(false);
        if (MenuButtonsRoot != null)
            MenuButtonsRoot.SetActive(true);

        // Disable event system
        if (EventSystem.current != null)
        {
            _prevSendNavEvents = EventSystem.current.sendNavigationEvents;
            EventSystem.current.sendNavigationEvents = false;
        }

        _focusedIndex = -1;
        _nav.Reset();
        FocusButton(FirstUsableIndex());
    }

    private void OnDisable()
    {
        WireButtons(false);
        DisableInput();

        // If the panel hides while settings is open (e.g. the run resumes), close settings too.
        if (SettingsPanel != null && SettingsPanel.gameObject.activeSelf)
            SettingsPanel.gameObject.SetActive(false);

        if (EventSystem.current != null)
        {
            EventSystem.current.sendNavigationEvents = _prevSendNavEvents;

            if (IsOwnButton(EventSystem.current.currentSelectedGameObject))
                EventSystem.current.SetSelectedGameObject(null);
        }
    }

    // Actions (also public so they can be invoked from the inspector if needed)

    public void Resume()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.ChangeState(EGameState.Running);
    }

    public void Abandon()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.ReturnToLobby();
    }

    public void OpenOptions()
    {
        if (SettingsPanel == null)
        {
            Debug.LogWarning("[PauseUIController] No SettingsPanel wired — Options does nothing.");
            return;
        }

        // Hand off to the settings page
        DisableInput();
        if (MenuButtonsRoot != null)
            MenuButtonsRoot.SetActive(false);

        SettingsPanel.Open(this);
    }

    /// <summary>Called back by the settings panel when it closes and estores the pause menu.</summary>
    public void OnSettingsClosed()
    {
        if (MenuButtonsRoot != null)
            MenuButtonsRoot.SetActive(true);

        EnableInput();

        _nav.Reset();
        _focusedIndex = -1;
        FocusButton(OptionsIndex);
    }

    public void Quit()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.Quit();
    }

    // Button wiring

    private void EnsureButtons()
    {
        if (_buttons == null)
            _buttons = new[] { ResumeButton, AbandonButton, OptionsButton, QuitButton };
    }

    private void WireButtons(bool wire)
    {
        SetListener(ResumeButton, Resume, wire);
        SetListener(AbandonButton, Abandon, wire);
        SetListener(OptionsButton, OpenOptions, wire);
        SetListener(QuitButton, Quit, wire);
    }

    private static void SetListener(Button button, UnityAction action, bool wire)
    {
        if (button == null)
            return;

        if (wire)
            button.onClick.AddListener(action);
        else
            button.onClick.RemoveListener(action);
    }

    // Navigation / input

    private void EnableInput()
    {
        _inputs ??= new GameInputs();

        // Remove before add so a second EnableInput can't double-subscribe the handlers — a doubled
        // OnNavigate steps focus twice per push.
        _inputs.UI.Navigate.performed -= OnNavigate;
        _inputs.UI.Submit.performed -= OnSubmit;
        _inputs.UI.Cancel.performed -= OnCancel;

        _inputs.UI.Navigate.performed += OnNavigate;
        _inputs.UI.Submit.performed += OnSubmit;
        _inputs.UI.Cancel.performed += OnCancel;
        _inputs.UI.Enable();
    }

    private void DisableInput()
    {
        if (_inputs == null)
            return;

        _inputs.UI.Navigate.performed -= OnNavigate;
        _inputs.UI.Submit.performed -= OnSubmit;
        _inputs.UI.Cancel.performed -= OnCancel;
        _inputs.UI.Disable();
    }

    private void OnNavigate(InputAction.CallbackContext ctx)
    {
        // One discrete step per push (hysteresis filters analog jitter); vertical menu, so up = previous
        // / down = next and horizontal is ignored.
        switch (_nav.Evaluate(ctx.ReadValue<Vector2>()))
        {
            case NavDirection.Up: StepFocus(-1); break;
            case NavDirection.Down: StepFocus(1); break;
        }
    }

    private void OnSubmit(InputAction.CallbackContext ctx)
    {
        if (_focusedIndex < 0 || _focusedIndex >= _buttons.Length)
            return;

        var button = _buttons[_focusedIndex];
        if (button != null && button.interactable)
            button.onClick.Invoke();
    }

    // Cancel (gamepad B) mirrors the Pause key: resume the run.
    private void OnCancel(InputAction.CallbackContext ctx) => Resume();

    private void StepFocus(int direction)
    {
        int count = _buttons.Length;
        int index = _focusedIndex < 0 ? 0 : _focusedIndex;

        // Walk in the requested direction, skipping null / non-interactable entries.
        for (int i = 0; i < count; i++)
        {
            index = ((index + direction) % count + count) % count;
            if (_buttons[index] != null && _buttons[index].interactable)
            {
                FocusButton(index);
                return;
            }
        }
    }

    private void FocusButton(int index)
    {
        if (index < 0 || index >= _buttons.Length)
            return;

        var button = _buttons[index];
        if (button == null || !button.interactable)
            return;

        _focusedIndex = index;

        // Selection drives the button's highlight; submit is still handled by us (nav events off).
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(button.gameObject);
    }

    private int FirstUsableIndex()
    {
        for (int i = 0; i < _buttons.Length; i++)
            if (_buttons[i] != null && _buttons[i].interactable)
                return i;
        return -1;
    }

    private bool IsOwnButton(GameObject go)
    {
        if (go == null)
            return false;

        for (int i = 0; i < _buttons.Length; i++)
            if (_buttons[i] != null && _buttons[i].gameObject == go)
                return true;
        return false;
    }
}